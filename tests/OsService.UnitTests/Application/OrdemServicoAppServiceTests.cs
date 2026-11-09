using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OsService.Application.AppServices;
using OsService.Application.DTOs;
using OsService.Application.Exceptions;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace OsService.UnitTests.Application
{
    public class OrdemServicoAppServiceTests
    {
        private static readonly DateTime Agora = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IOrdemServicoRepository> _repositorio = new();
        private readonly Mock<IClienteVeiculoGateway> _gateway = new();
        private readonly Mock<IMonitoramentoService> _monitoramento = new();
        private readonly Mock<IContextoRequisicao> _contexto = new();
        private readonly Mock<IRelogio> _relogio = new();

        public OrdemServicoAppServiceTests()
        {
            _relogio.Setup(r => r.UtcAgora).Returns(Agora);
            _contexto.Setup(c => c.CorrelationId).Returns("req-1");
        }

        private OrdemServicoAppService Criar(bool sagaHabilitada = false) =>
            new(_repositorio.Object, _gateway.Object, _monitoramento.Object, _contexto.Object, _relogio.Object,
                MsOptions.Create(new SagaOptions { Habilitada = sagaHabilitada }),
                NullLogger<OrdemServicoAppService>.Instance);

        private static OrdemServico NovaOrdem(StatusOrdemServico status = StatusOrdemServico.Recebida)
        {
            var ordem = OrdemServico.Abrir(1, 2, "Barulho no motor", null, Agora.AddHours(-1), null);

            var caminho = status switch
            {
                StatusOrdemServico.Recebida            => [],
                StatusOrdemServico.EmDiagnostico       => [StatusOrdemServico.EmDiagnostico],
                StatusOrdemServico.AguardandoAprovacao => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao],
                StatusOrdemServico.EmExecucao          => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao],
                StatusOrdemServico.Finalizada          => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao, StatusOrdemServico.Finalizada],
                StatusOrdemServico.Cancelada           => new[] { StatusOrdemServico.Cancelada },
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };

            foreach (var s in caminho)
                ordem.AlterarStatus(s, Agora.AddMinutes(-30), OrigemAlteracaoStatus.Usuario);

            return ordem;
        }

        private void RepositorioDevolve(OrdemServico ordem) =>
            _repositorio.Setup(r => r.ObterPorIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ordem);

        // ── Abertura ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Abrir_confere_o_veiculo_salva_e_registra_o_evento()
        {
            _gateway.Setup(g => g.ObterVeiculoAsync(2, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new VeiculoResumo(2, 1, "ABC1D23"));

            var dto = await Criar().AbrirAsync(new AbrirOrdemServicoDto(1, 2, "Barulho no motor", 300m));

            Assert.Equal(StatusOrdemServico.Recebida, dto.Status);
            Assert.Equal(300m, dto.ValorTotalEstimado);
            _repositorio.Verify(r => r.AdicionarAsync(It.IsAny<OrdemServico>(), It.IsAny<CancellationToken>()), Times.Once);
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Once);
            _monitoramento.Verify(m => m.RegistrarOrdemServicoCriada(It.IsAny<OrdemServico>()), Times.Once);
        }

        [Fact]
        public async Task Abrir_com_veiculo_inexistente_lanca_nao_encontrado()
        {
            _gateway.Setup(g => g.ObterVeiculoAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync((VeiculoResumo?)null);

            await Assert.ThrowsAsync<RecursoNaoEncontradoException>(() =>
                Criar().AbrirAsync(new AbrirOrdemServicoDto(1, 2, "Barulho", null)));
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Abrir_com_veiculo_de_outro_cliente_lanca_regra_de_negocio()
        {
            _gateway.Setup(g => g.ObterVeiculoAsync(2, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new VeiculoResumo(2, 99, null));

            await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
                Criar().AbrirAsync(new AbrirOrdemServicoDto(1, 2, "Barulho", null)));
        }

        [Fact]
        public async Task Abrir_propaga_a_indisponibilidade_do_MechanicLtda()
        {
            _gateway.Setup(g => g.ObterVeiculoAsync(2, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new IntegracaoIndisponivelException("fora do ar"));

            await Assert.ThrowsAsync<IntegracaoIndisponivelException>(() =>
                Criar().AbrirAsync(new AbrirOrdemServicoDto(1, 2, "Barulho", null)));
        }

        // ── Dados e consultas ──────────────────────────────────────────────────

        [Fact]
        public async Task Atualizar_com_valor_estimado_leva_a_OS_recebida_para_diagnostico()
        {
            var ordem = NovaOrdem();
            RepositorioDevolve(ordem);

            var dto = await Criar().AtualizarAsync(10, new AtualizarOrdemServicoDto("Novo texto", 500m));

            Assert.Equal(StatusOrdemServico.EmDiagnostico, dto.Status);
            Assert.Equal("Novo texto", dto.DescricaoProblema);
            _monitoramento.Verify(m => m.RegistrarMudancaStatus(ordem, StatusOrdemServico.Recebida, It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task Atualizar_sem_valor_estimado_mantem_o_status()
        {
            RepositorioDevolve(NovaOrdem());

            var dto = await Criar().AtualizarAsync(10, new AtualizarOrdemServicoDto("Novo texto", null));

            Assert.Equal(StatusOrdemServico.Recebida, dto.Status);
        }

        [Fact]
        public async Task Obter_OS_inexistente_lanca_nao_encontrado()
        {
            await Assert.ThrowsAsync<RecursoNaoEncontradoException>(() => Criar().ObterAsync(10));
        }

        [Fact]
        public async Task Listagem_tira_as_encerradas_e_ordena_por_prioridade()
        {
            var recebida   = NovaOrdem();
            var execucao   = NovaOrdem(StatusOrdemServico.EmExecucao);
            var aprovacao  = NovaOrdem(StatusOrdemServico.AguardandoAprovacao);
            var finalizada = NovaOrdem(StatusOrdemServico.Finalizada);
            var cancelada  = NovaOrdem(StatusOrdemServico.Cancelada);
            _repositorio.Setup(r => r.ObterTodasAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync([recebida, finalizada, aprovacao, cancelada, execucao]);

            var lista = await Criar().ListarEmAndamentoAsync();

            Assert.Equal([StatusOrdemServico.EmExecucao, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.Recebida],
                         lista.Select(o => o.Status));
        }

        [Fact]
        public async Task Historico_vem_em_ordem_cronologica()
        {
            RepositorioDevolve(NovaOrdem(StatusOrdemServico.AguardandoAprovacao));

            var historico = await Criar().ObterHistoricoAsync(10);

            Assert.Equal([StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao],
                         historico.Select(h => h.StatusNovo));
            Assert.Null(historico[0].StatusAnterior);
        }

        [Fact]
        public async Task Tempo_medio_e_arredondado_e_convertido_para_horas()
        {
            _repositorio.Setup(r => r.ObterTempoMedioExecucaoAsync(It.IsAny<CancellationToken>())).ReturnsAsync((3, 90.456));

            var dto = await Criar().ObterTempoMedioExecucaoAsync();

            Assert.Equal(3, dto.QuantidadeOrdensFinalizadas);
            Assert.Equal(90.46, dto.TempoMedioMinutos);
            Assert.Equal(1.51, dto.TempoMedioHoras);
        }

        [Fact]
        public async Task Lista_por_cliente_usa_o_repositorio()
        {
            _repositorio.Setup(r => r.ObterPorClienteAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([NovaOrdem()]);

            Assert.Single(await Criar().ListarPorClienteAsync(1));
        }

        [Fact]
        public async Task Remover_OS_recebida_remove_e_salva()
        {
            var ordem = NovaOrdem();
            RepositorioDevolve(ordem);

            await Criar().RemoverAsync(10);

            _repositorio.Verify(r => r.Remover(ordem), Times.Once);
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Remover_OS_que_ja_passou_do_diagnostico_e_bloqueado()
        {
            RepositorioDevolve(NovaOrdem(StatusOrdemServico.AguardandoAprovacao));

            await Assert.ThrowsAsync<RegraDeNegocioException>(() => Criar().RemoverAsync(10));
            _repositorio.Verify(r => r.Remover(It.IsAny<OrdemServico>()), Times.Never);
        }

        // ── Transições ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Fluxo_manual_completo_com_a_saga_desligada()
        {
            var ordem = NovaOrdem();
            RepositorioDevolve(ordem);
            var app = Criar();

            await app.IniciarDiagnosticoAsync(10);
            await app.AguardarAprovacaoAsync(10);
            await app.AprovarAsync(10);
            await app.FinalizarAsync(10);
            var dto = await app.EntregarAsync(10);

            Assert.Equal(StatusOrdemServico.Entregue, dto.Status);
            Assert.Equal(6, ordem.Historico.Count);
            Assert.All(ordem.Historico, h => Assert.Equal(OrigemAlteracaoStatus.Usuario, h.Origem));
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Exactly(5));
            _monitoramento.Verify(m => m.RegistrarMudancaStatus(ordem, It.IsAny<StatusOrdemServico>(), It.IsAny<TimeSpan>()), Times.Exactly(5));
        }

        [Fact]
        public async Task Recusar_cancela_a_OS_com_o_motivo_no_historico()
        {
            var ordem = NovaOrdem(StatusOrdemServico.AguardandoAprovacao);
            RepositorioDevolve(ordem);

            var dto = await Criar().RecusarAsync(10, "Caro demais");

            Assert.Equal(StatusOrdemServico.Cancelada, dto.Status);
            Assert.Equal("Orçamento recusado: Caro demais", ordem.Historico.Last().Motivo);
            Assert.Equal("req-1", ordem.Historico.Last().CorrelationId);
        }

        [Fact]
        public async Task Recusar_sem_motivo_usa_o_texto_padrao()
        {
            var ordem = NovaOrdem(StatusOrdemServico.AguardandoAprovacao);
            RepositorioDevolve(ordem);

            await Criar().RecusarAsync(10, " ");

            Assert.Equal("Orçamento recusado", ordem.Historico.Last().Motivo);
        }

        [Fact]
        public async Task Transicao_invalida_lanca_regra_de_negocio_e_nao_salva()
        {
            RepositorioDevolve(NovaOrdem());

            await Assert.ThrowsAsync<RegraDeNegocioException>(() => Criar().FinalizarAsync(10));
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("aguardar-aprovacao")]
        [InlineData("aprovar")]
        [InlineData("recusar")]
        [InlineData("finalizar")]
        public async Task Com_a_saga_ligada_as_transicoes_dela_ficam_bloqueadas(string operacao)
        {
            RepositorioDevolve(NovaOrdem(StatusOrdemServico.AguardandoAprovacao));
            var app = Criar(sagaHabilitada: true);

            Func<Task> chamada = operacao switch
            {
                "aguardar-aprovacao" => () => app.AguardarAprovacaoAsync(10),
                "aprovar"            => () => app.AprovarAsync(10),
                "recusar"            => () => app.RecusarAsync(10, null),
                _                    => () => app.FinalizarAsync(10),
            };

            var ex = await Assert.ThrowsAsync<OperacaoControladaPelaSagaException>(chamada);
            Assert.Contains(operacao, ex.Message);
        }

        [Fact]
        public async Task Com_a_saga_ligada_o_diagnostico_e_a_entrega_continuam_manuais()
        {
            var ordem = NovaOrdem();
            RepositorioDevolve(ordem);

            var dto = await Criar(sagaHabilitada: true).IniciarDiagnosticoAsync(10);

            Assert.Equal(StatusOrdemServico.EmDiagnostico, dto.Status);
        }

        [Fact]
        public async Task Cancelar_antes_do_orcamento_e_permitido_mesmo_com_a_saga()
        {
            var ordem = NovaOrdem(StatusOrdemServico.EmDiagnostico);
            RepositorioDevolve(ordem);

            var dto = await Criar(sagaHabilitada: true).CancelarAsync(10, null);

            Assert.Equal(StatusOrdemServico.Cancelada, dto.Status);
            Assert.Equal("Cancelada manualmente", ordem.Historico.Last().Motivo);
        }

        [Fact]
        public async Task Cancelar_depois_do_orcamento_com_a_saga_ligada_e_bloqueado()
        {
            RepositorioDevolve(NovaOrdem(StatusOrdemServico.AguardandoAprovacao));

            await Assert.ThrowsAsync<OperacaoControladaPelaSagaException>(() =>
                Criar(sagaHabilitada: true).CancelarAsync(10, "Cliente desistiu"));
        }

        [Fact]
        public async Task Cancelar_depois_do_orcamento_com_a_saga_desligada_e_permitido()
        {
            var ordem = NovaOrdem(StatusOrdemServico.AguardandoAprovacao);
            RepositorioDevolve(ordem);

            var dto = await Criar().CancelarAsync(10, "Cliente desistiu");

            Assert.Equal(StatusOrdemServico.Cancelada, dto.Status);
            Assert.Equal("Cliente desistiu", ordem.Historico.Last().Motivo);
        }

        // ── Itens ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task Itens_podem_ser_adicionados_atualizados_e_removidos()
        {
            var ordem = NovaOrdem(StatusOrdemServico.EmDiagnostico);
            RepositorioDevolve(ordem);
            var app = Criar();

            var adicionado = await app.AdicionarItemAsync(10,
                new ItemOrdemServicoInputDto("Filtro de óleo", TipoItemOrdemServico.Peca, 2, 35m, 7, null));
            Assert.Equal(70m, adicionado.ValorTotal);

            var atualizado = await app.AtualizarItemAsync(10, adicionado.Id,
                new ItemOrdemServicoInputDto("Filtro de óleo", TipoItemOrdemServico.Peca, 3, 35m, 7, null));
            Assert.Equal(105m, atualizado.ValorTotal);

            Assert.Single(await app.ListarItensAsync(10));
            Assert.Equal("Filtro de óleo", (await app.ObterItemAsync(10, adicionado.Id)).Descricao);

            await app.RemoverItemAsync(10, adicionado.Id);
            Assert.Empty(ordem.Itens);
            _repositorio.Verify(r => r.SalvarAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public async Task Itens_nao_mudam_depois_do_diagnostico()
        {
            RepositorioDevolve(NovaOrdem(StatusOrdemServico.AguardandoAprovacao));

            await Assert.ThrowsAsync<RegraDeNegocioException>(() => Criar().AdicionarItemAsync(10,
                new ItemOrdemServicoInputDto("Filtro", TipoItemOrdemServico.Peca, 1, 10m, null, null)));
        }
    }
}
