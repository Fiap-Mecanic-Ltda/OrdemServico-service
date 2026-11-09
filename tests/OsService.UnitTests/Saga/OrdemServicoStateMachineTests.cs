using MassTransit;
using MassTransit.Testing;
using MechanicLtda.Contratos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Application.Saga;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Infrastructure;
using OsService.Infrastructure.Repositories;
using ItemContrato = MechanicLtda.Contratos.ItemOrdemServico;

namespace OsService.UnitTests.Saga
{
    /// <summary>
    /// A state machine com o test harness do MassTransit (transporte e repositório da Saga em
    /// memória, OS no EF InMemory). Os eventos de Billing e Execução são publicados à mão, um
    /// por vez, para conferir cada passo e cada compensação.
    /// </summary>
    public sealed class OrdemServicoStateMachineTests : IAsyncLifetime
    {
        private static readonly DateTime Inicio = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IMonitoramentoService> _monitoramento = new();
        private readonly RelogioFalso _relogio = new() { UtcAgora = Inicio };
        private ServiceProvider _provider = null!;
        private ITestHarness _harness = null!;
        private ISagaStateMachineTestHarness<OrdemServicoStateMachine, OrdemServicoSagaState> _saga = null!;

        private sealed class RelogioFalso : IRelogio
        {
            public DateTime UtcAgora { get; set; }
        }

        public async Task InitializeAsync()
        {
            var banco = Guid.NewGuid().ToString();

            _provider = new ServiceCollection()
                .AddLogging()
                .AddDbContext<OsServiceContext>(o => o.UseInMemoryDatabase(banco))
                .AddScoped<IOrdemServicoRepository, OrdemServicoRepository>()
                .AddSingleton<IRelogio>(_relogio)
                .AddSingleton(_monitoramento.Object)
                .Configure<SagaOptions>(o => o.PrazoAprovacaoHoras = 72)
                .AddMassTransitTestHarness(x =>
                    x.AddSagaStateMachine<OrdemServicoStateMachine, OrdemServicoSagaState>().InMemoryRepository())
                .BuildServiceProvider(true);

            _harness = _provider.GetRequiredService<ITestHarness>();
            _harness.TestTimeout = TimeSpan.FromSeconds(5);
            await _harness.Start();
            _saga = _harness.GetSagaStateMachineHarness<OrdemServicoStateMachine, OrdemServicoSagaState>();
        }

        public async Task DisposeAsync()
        {
            await _harness.Stop();
            await _provider.DisposeAsync();
        }

        // ── Apoio ──────────────────────────────────────────────────────────────

        /// <summary>OS gravada em diagnóstico e Saga iniciada, como a API faz.</summary>
        private async Task<OrdemServico> IniciarAsync()
        {
            OrdemServico ordem;
            using (var scope = _provider.CreateScope())
            {
                var contexto = scope.ServiceProvider.GetRequiredService<OsServiceContext>();
                ordem = OrdemServico.Abrir(1, 2, "Barulho no freio", null, Inicio, "req-1");
                ordem.AlterarStatus(StatusOrdemServico.EmDiagnostico, Inicio, OrigemAlteracaoStatus.Usuario);
                contexto.Add(ordem);
                await contexto.SaveChangesAsync();
            }

            await _harness.Bus.Publish(new DiagnosticoSolicitado
            {
                CorrelationId           = ordem.CorrelationId,
                OrdemServicoId          = ordem.Id,
                ClienteId               = 1,
                VeiculoId               = 2,
                DescricaoProblema       = ordem.DescricaoProblema,
                NomeCliente             = "Ana",
                EmailCliente            = "ana@x.com",
                CorrelationIdRequisicao = "req-1",
            });
            await EsperarEstadoAsync(ordem, m => m.Diagnosticando);

            return ordem;
        }

        private async Task EsperarEstadoAsync(OrdemServico ordem, Func<OrdemServicoStateMachine, State> estado) =>
            Assert.NotNull(await _saga.Exists(ordem.CorrelationId, estado));

        private Task EnviarAsync<T>(OrdemServico ordem, T evento) where T : MensagemSaga =>
            _harness.Bus.Publish(evento with { CorrelationId = ordem.CorrelationId, OrdemServicoId = ordem.Id, DataHora = _relogio.UtcAgora });

        /// <summary>Publica o evento e espera a Saga consumi-lo.</summary>
        private async Task ProcessarAsync<T>(OrdemServico ordem, T evento) where T : MensagemSaga
        {
            await EnviarAsync(ordem, evento);
            Assert.True(await _saga.Consumed.Any<T>(c => c.Context.Message.CorrelationId == ordem.CorrelationId));
        }

        private async Task<OrdemServico> LerAsync(OrdemServico ordem)
        {
            using var scope = _provider.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IOrdemServicoRepository>().ObterPorIdAsync(ordem.Id))!;
        }

        private static DiagnosticoConcluido Diagnostico(string tipo = "Peca") => new()
        {
            Itens = [new ItemContrato { Descricao = "Pastilha", Tipo = tipo, Quantidade = 2, ValorUnitario = 80m, EstoqueId = 3 }],
        };

        private async Task<OrdemServico> AteAprovacaoAsync(DateTime? validade = null)
        {
            var ordem = await IniciarAsync();
            await EnviarAsync(ordem, Diagnostico());
            await EsperarEstadoAsync(ordem, m => m.AguardandoOrcamento);
            await EnviarAsync(ordem, new OrcamentoGerado { OrcamentoId = 77, ValorTotal = 160m, Validade = validade });
            await EsperarEstadoAsync(ordem, m => m.AguardandoAprovacao);
            return ordem;
        }

        // ── Passos ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Inicio_pede_o_diagnostico_com_os_cabecalhos_de_correlacao()
        {
            var ordem = await IniciarAsync();

            var publicada = _harness.Published.Select<IniciarDiagnostico>().Single();
            Assert.Equal(ordem.CorrelationId, publicada.Context.Message.CorrelationId);
            Assert.Equal(ordem.Id, publicada.Context.Message.OrdemServicoId);
            Assert.Equal("Barulho no freio", publicada.Context.Message.DescricaoProblema);
            Assert.Equal(ordem.CorrelationId.ToString(), publicada.Context.Headers.Get<string>(CabecalhosSaga.CorrelationId));
            Assert.Equal("req-1", publicada.Context.Headers.Get<string>(CabecalhosSaga.CorrelationIdRequisicao));

            var estado = _saga.Sagas.Contains(ordem.CorrelationId)!;
            Assert.Equal(Inicio.AddHours(48), estado.PrazoDiagnostico);
        }

        [Fact]
        public async Task Diagnostico_concluido_grava_os_itens_e_pede_o_orcamento_com_os_dados_do_cliente()
        {
            var ordem = await IniciarAsync();

            await EnviarAsync(ordem, Diagnostico());
            await EsperarEstadoAsync(ordem, m => m.AguardandoOrcamento);

            var lida = await LerAsync(ordem);
            Assert.Equal(StatusOrdemServico.AguardandoOrcamento, lida.Status);
            Assert.Equal(160m, lida.ValorTotalItens);
            Assert.Equal(OrigemAlteracaoStatus.Saga, lida.Historico.OrderBy(h => h.Id).Last().Origem);

            Assert.True(await _harness.Published.Any<GerarOrcamento>(p =>
                p.Context.Message.NomeCliente == "Ana" && p.Context.Message.EmailCliente == "ana@x.com"
                && p.Context.Message.Itens.Single().Tipo == "Peca"));
            Assert.True(await _harness.Published.Any<OrdemServicoStatusAlterado>(p =>
                p.Context.Message.StatusAnterior == "EmDiagnostico" && p.Context.Message.StatusAtual == "AguardandoOrcamento"));
            _monitoramento.Verify(m => m.RegistrarPassoSaga(ordem.CorrelationId, ordem.Id, nameof(DiagnosticoConcluido), "AguardandoOrcamento"));
        }

        [Fact]
        public async Task Orcamento_gerado_usa_a_validade_informada_como_prazo_de_aprovacao()
        {
            var ordem = await AteAprovacaoAsync(validade: Inicio.AddDays(5));

            var estado = _saga.Sagas.Contains(ordem.CorrelationId)!;
            Assert.Equal(Inicio.AddDays(5), estado.PrazoAprovacao);
            Assert.Equal(77, (await LerAsync(ordem)).OrcamentoId);
        }

        [Fact]
        public async Task Sem_validade_o_prazo_de_aprovacao_vem_da_configuracao()
        {
            var ordem = await AteAprovacaoAsync();

            Assert.Equal(Inicio.AddHours(72), _saga.Sagas.Contains(ordem.CorrelationId)!.PrazoAprovacao);
        }

        [Fact]
        public async Task Orcamento_aprovado_pede_o_pagamento_do_valor_do_orcamento()
        {
            var ordem = await AteAprovacaoAsync();

            await EnviarAsync(ordem, new OrcamentoAprovado { OrcamentoId = 77 });
            await EsperarEstadoAsync(ordem, m => m.AguardandoPagamento);

            Assert.True(await _harness.Published.Any<SolicitarPagamento>(p =>
                p.Context.Message.OrcamentoId == 77 && p.Context.Message.ValorTotal == 160m));
            Assert.Equal(StatusOrdemServico.AguardandoPagamento, (await LerAsync(ordem)).Status);
        }

        [Fact]
        public async Task Execucao_finalizada_usa_as_datas_da_execucao_e_termina_a_saga()
        {
            var ordem = await AteAprovacaoAsync();
            await EnviarAsync(ordem, new OrcamentoAprovado { OrcamentoId = 77 });
            await EsperarEstadoAsync(ordem, m => m.AguardandoPagamento);
            await EnviarAsync(ordem, new PagamentoConfirmado { PagamentoId = 88, Valor = 160m });
            await EsperarEstadoAsync(ordem, m => m.Executando);
            Assert.True(await _harness.Published.Any<IniciarExecucao>(p => p.Context.Message.OrcamentoId == 77));

            var inicio = Inicio.AddHours(1);
            await ProcessarAsync(ordem, new ExecucaoIniciada { DataInicio = inicio });
            await EnviarAsync(ordem, new ExecucaoFinalizada { DataInicio = inicio, DataFim = inicio.AddHours(2) });
            await EsperarSemSagaAsync(ordem);

            var lida = await LerAsync(ordem);
            Assert.Equal(StatusOrdemServico.Finalizada, lida.Status);
            Assert.Equal(88, lida.PagamentoId);
            Assert.Equal(TimeSpan.FromHours(2), lida.TempoExecucao);
        }

        // ── Compensações ───────────────────────────────────────────────────────

        [Fact]
        public async Task Falha_no_diagnostico_devolve_a_OS_para_recebida_sem_compensacao()
        {
            var ordem = await IniciarAsync();

            await EnviarAsync(ordem, new DiagnosticoFalhou { Motivo = "Sem acesso ao veículo" });
            await EsperarSemSagaAsync(ordem);

            var lida = await LerAsync(ordem);
            Assert.Equal(StatusOrdemServico.Recebida, lida.Status);
            Assert.Equal("Diagnóstico falhou: Sem acesso ao veículo", lida.Historico.OrderBy(h => h.Id).Last().Motivo);
            Assert.False(_harness.Published.Select<LiberarReservaEstoque>().Any());
        }

        [Fact]
        public async Task Prazo_do_diagnostico_libera_o_estoque_e_devolve_a_OS_para_recebida()
        {
            var ordem = await IniciarAsync();

            await _harness.Bus.Publish(new PrazoDiagnosticoExpirado { CorrelationId = ordem.CorrelationId });
            await EsperarSemSagaAsync(ordem);

            Assert.Equal(StatusOrdemServico.Recebida, (await LerAsync(ordem)).Status);
            Assert.True(await _harness.Published.Any<LiberarReservaEstoque>(p => p.Context.Message.Motivo == "Prazo do diagnóstico expirado"));
            _monitoramento.Verify(m => m.RegistrarCompensacaoSaga(ordem.CorrelationId, ordem.Id, "Prazo do diagnóstico expirado",
                It.Is<IReadOnlyCollection<string>>(c => c.Single() == nameof(LiberarReservaEstoque))));
        }

        [Fact]
        public async Task Cancelamento_manual_no_diagnostico_libera_o_estoque_e_termina_a_saga()
        {
            var ordem = await IniciarAsync();

            await _harness.Bus.Publish(new OrdemServicoCanceladaManualmente { CorrelationId = ordem.CorrelationId, Motivo = "Cliente desistiu" });
            await EsperarSemSagaAsync(ordem);

            Assert.True(await _harness.Published.Any<LiberarReservaEstoque>(p => p.Context.Message.Motivo == "Cliente desistiu"));
        }

        [Fact]
        public async Task Falha_no_orcamento_volta_para_diagnostico_e_pede_um_novo()
        {
            var ordem = await IniciarAsync();
            await EnviarAsync(ordem, Diagnostico());
            await EsperarEstadoAsync(ordem, m => m.AguardandoOrcamento);

            await ProcessarAsync(ordem, new OrcamentoFalhou { Motivo = "Item sem preço" });
            await EsperarEstadoAsync(ordem, m => m.Diagnosticando);

            Assert.Equal(StatusOrdemServico.EmDiagnostico, (await LerAsync(ordem)).Status);
            Assert.Equal(2, _harness.Published.Select<IniciarDiagnostico>().Count());
        }

        [Fact]
        public async Task Recusa_libera_o_estoque_e_cancela_a_OS()
        {
            var ordem = await AteAprovacaoAsync();

            await EnviarAsync(ordem, new OrcamentoRecusado { OrcamentoId = 77, Motivo = "Caro" });
            await EsperarSemSagaAsync(ordem);

            var lida = await LerAsync(ordem);
            Assert.Equal(StatusOrdemServico.Cancelada, lida.Status);
            Assert.Equal("Orçamento recusado pelo cliente: Caro", lida.Historico.OrderBy(h => h.Id).Last().Motivo);
            Assert.True(await _harness.Published.Any<LiberarReservaEstoque>());
            Assert.False(_harness.Published.Select<CancelarOrcamento>().Any());
        }

        [Fact]
        public async Task Prazo_de_aprovacao_cancela_o_orcamento_e_espera_a_confirmacao()
        {
            var ordem = await AteAprovacaoAsync();

            await _harness.Bus.Publish(new PrazoAprovacaoExpirado { CorrelationId = ordem.CorrelationId });
            await EsperarEstadoAsync(ordem, m => m.Compensando);

            Assert.Equal(StatusOrdemServico.Compensando, (await LerAsync(ordem)).Status);
            Assert.True(await _harness.Published.Any<CancelarOrcamento>(p => p.Context.Message.OrcamentoId == 77));
            Assert.True(await _harness.Published.Any<LiberarReservaEstoque>());

            await EnviarAsync(ordem, new OrcamentoCancelado { OrcamentoId = 77 });
            await EsperarSemSagaAsync(ordem);
            Assert.Equal(StatusOrdemServico.Cancelada, (await LerAsync(ordem)).Status);
        }

        [Fact]
        public async Task Falha_na_execucao_so_termina_com_estorno_e_cancelamento_confirmados()
        {
            var ordem = await AteAprovacaoAsync();
            await EnviarAsync(ordem, new OrcamentoAprovado { OrcamentoId = 77 });
            await EsperarEstadoAsync(ordem, m => m.AguardandoPagamento);
            await EnviarAsync(ordem, new PagamentoConfirmado { PagamentoId = 88, Valor = 160m });
            await EsperarEstadoAsync(ordem, m => m.Executando);

            await EnviarAsync(ordem, new ExecucaoFalhou { Motivo = "Peça com defeito" });
            await EsperarEstadoAsync(ordem, m => m.Compensando);
            Assert.True(await _harness.Published.Any<EstornarPagamento>(p => p.Context.Message.PagamentoId == 88));
            Assert.True(await _harness.Published.Any<CancelarOrcamento>(p => p.Context.Message.OrcamentoId == 77));

            await ProcessarAsync(ordem, new PagamentoEstornado { PagamentoId = 88 });
            await EsperarEstadoAsync(ordem, m => m.Compensando);
            Assert.Equal(StatusOrdemServico.Compensando, (await LerAsync(ordem)).Status);

            await EnviarAsync(ordem, new OrcamentoCancelado { OrcamentoId = 77 });
            await EsperarSemSagaAsync(ordem);
            Assert.Equal(StatusOrdemServico.Cancelada, (await LerAsync(ordem)).Status);
        }

        // ── Mensagens inesperadas ──────────────────────────────────────────────

        [Fact]
        public async Task Evento_fora_de_ordem_e_ignorado_sem_falha()
        {
            var ordem = await IniciarAsync();

            await ProcessarAsync(ordem, new OrcamentoAprovado { OrcamentoId = 77 });

            await EsperarEstadoAsync(ordem, m => m.Diagnosticando);
            Assert.False(_harness.Published.Select<Fault<OrcamentoAprovado>>().Any());
            Assert.Equal(StatusOrdemServico.EmDiagnostico, (await LerAsync(ordem)).Status);
        }

        [Fact]
        public async Task Evento_de_saga_inexistente_e_descartado()
        {
            await _harness.Bus.Publish(new OrcamentoAprovado { CorrelationId = Guid.NewGuid(), OrdemServicoId = 999, OrcamentoId = 1 });

            Assert.True(await _harness.Consumed.Any<OrcamentoAprovado>());
            Assert.False(_harness.Published.Select<Fault<OrcamentoAprovado>>().Any());
        }

        [Fact]
        public async Task Item_com_tipo_invalido_falha_a_mensagem_e_mantem_o_diagnostico()
        {
            var ordem = await IniciarAsync();

            await EnviarAsync(ordem, Diagnostico(tipo: "Parafuso"));

            Assert.True(await _harness.Published.Any<Fault<DiagnosticoConcluido>>());
            await EsperarEstadoAsync(ordem, m => m.Diagnosticando);
            Assert.Empty((await LerAsync(ordem)).Itens);
        }

        private async Task EsperarSemSagaAsync(OrdemServico ordem) =>
            Assert.Null(await _saga.NotExists(ordem.CorrelationId));
    }
}
