using MassTransit;
using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;
using ItemContrato = MechanicLtda.Contratos.ItemOrdemServico;

namespace OsService.Application.Saga
{
    /// <summary>
    /// Saga orquestrada da OS (ADR-002). Cada evento recebido dos outros serviços muda o status
    /// da OS, grava o histórico com origem "Saga" e publica o próximo comando. As falhas
    /// disparam as compensações da tabela do ADR. Eventos fora de ordem ou repetidos são
    /// ignorados, e eventos de uma Saga que já terminou são descartados.
    /// </summary>
    public class OrdemServicoStateMachine : MassTransitStateMachine<OrdemServicoSagaState>
    {
        // Estados da Saga (o status da OS fica na própria OS).
        public State Diagnosticando { get; private set; } = null!;
        public State AguardandoOrcamento { get; private set; } = null!;
        public State AguardandoAprovacao { get; private set; } = null!;
        public State Executando { get; private set; } = null!;
        public State Compensando { get; private set; } = null!;

        // Internos
        public Event<DiagnosticoSolicitado> DiagnosticoSolicitado { get; private set; } = null!;
        public Event<OrdemServicoCanceladaManualmente> CanceladaManualmente { get; private set; } = null!;
        public Event<PrazoDiagnosticoExpirado> PrazoDiagnosticoExpirado { get; private set; } = null!;
        public Event<PrazoAprovacaoExpirado> PrazoAprovacaoExpirado { get; private set; } = null!;

        // Execução
        public Event<DiagnosticoConcluido> DiagnosticoConcluido { get; private set; } = null!;
        public Event<DiagnosticoFalhou> DiagnosticoFalhou { get; private set; } = null!;
        public Event<ExecucaoIniciada> ExecucaoIniciada { get; private set; } = null!;
        public Event<ExecucaoFinalizada> ExecucaoFinalizada { get; private set; } = null!;
        public Event<ExecucaoFalhou> ExecucaoFalhou { get; private set; } = null!;

        // Billing
        public Event<OrcamentoGerado> OrcamentoGerado { get; private set; } = null!;
        public Event<OrcamentoFalhou> OrcamentoFalhou { get; private set; } = null!;
        public Event<OrcamentoAprovado> OrcamentoAprovado { get; private set; } = null!;
        public Event<OrcamentoRecusado> OrcamentoRecusado { get; private set; } = null!;
        public Event<PagamentoConfirmado> PagamentoConfirmado { get; private set; } = null!;
        public Event<PagamentoFalhou> PagamentoFalhou { get; private set; } = null!;
        public Event<OrcamentoCancelado> OrcamentoCancelado { get; private set; } = null!;
        public Event<PagamentoEstornado> PagamentoEstornado { get; private set; } = null!;

        public OrdemServicoStateMachine()
        {
            InstanceState(x => x.EstadoAtual);

            Event(() => DiagnosticoSolicitado, e => e.CorrelateById(c => c.Message.CorrelationId));
            Interno(() => CanceladaManualmente, m => m.CorrelationId);
            Interno(() => PrazoDiagnosticoExpirado, m => m.CorrelationId);
            Interno(() => PrazoAprovacaoExpirado, m => m.CorrelationId);

            Externo(() => DiagnosticoConcluido);
            Externo(() => DiagnosticoFalhou);
            Externo(() => ExecucaoIniciada);
            Externo(() => ExecucaoFinalizada);
            Externo(() => ExecucaoFalhou);
            Externo(() => OrcamentoGerado);
            Externo(() => OrcamentoFalhou);
            Externo(() => OrcamentoAprovado);
            Externo(() => OrcamentoRecusado);
            Externo(() => PagamentoConfirmado);
            Externo(() => PagamentoFalhou);
            Externo(() => OrcamentoCancelado);
            Externo(() => PagamentoEstornado);

            // Evento repetido ou fora de ordem (at-least-once): registra e segue.
            OnUnhandledEvent(ctx =>
            {
                ctx.GetServiceOrCreateInstance<ILogger<OrdemServicoStateMachine>>().LogWarning(
                    "Saga {CorrelationId}: evento {Evento} ignorado no estado {Estado}.",
                    ctx.Saga.CorrelationId, ctx.Event.Name, ctx.Saga.EstadoAtual);
                return ctx.Ignore();
            });

            Initially(
                When(DiagnosticoSolicitado)
                    .Then(ctx =>
                    {
                        var m = ctx.Message;
                        ctx.Saga.OrdemServicoId          = m.OrdemServicoId;
                        ctx.Saga.ClienteId               = m.ClienteId;
                        ctx.Saga.VeiculoId               = m.VeiculoId;
                        ctx.Saga.NomeCliente             = m.NomeCliente;
                        ctx.Saga.EmailCliente            = m.EmailCliente;
                        ctx.Saga.CorrelationIdRequisicao = m.CorrelationIdRequisicao;
                        ctx.Saga.DataInicio              = Agora(ctx);
                    })
                    .ThenAsync(ctx => SolicitarDiagnosticoAsync(ctx, ctx.Message.DescricaoProblema))
                    .TransitionTo(Diagnosticando)
                    .Then(ctx => Passo(ctx, nameof(DiagnosticoSolicitado))));

            During(Diagnosticando,
                When(DiagnosticoConcluido)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        ordem.SubstituirItens(ctx.Message.Itens.Select(ParaDominio), Agora(ctx));
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.AguardandoOrcamento, "Diagnóstico concluído");

                        ctx.Saga.PrazoDiagnostico = null;
                        await PublicarAsync(ctx, new GerarOrcamento
                        {
                            ClienteId    = ctx.Saga.ClienteId,
                            NomeCliente  = ctx.Saga.NomeCliente,
                            EmailCliente = ctx.Saga.EmailCliente,
                            Itens        = ordem.Itens.Select(ParaContrato).ToList(),
                        });
                        await SalvarAsync(ctx);
                    })
                    .TransitionTo(AguardandoOrcamento)
                    .Then(ctx => Passo(ctx, nameof(DiagnosticoConcluido))),

                When(DiagnosticoFalhou)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Recebida, $"Diagnóstico falhou: {ctx.Message.Motivo}");
                        await SalvarAsync(ctx);
                    })
                    .Then(ctx => Passo(ctx, nameof(DiagnosticoFalhou)))
                    .Finalize(),

                When(PrazoDiagnosticoExpirado)
                    .ThenAsync(async ctx =>
                    {
                        const string motivo = "Prazo do diagnóstico expirado";
                        await PublicarAsync(ctx, new LiberarReservaEstoque { Motivo = motivo });
                        var ordem = await ObterOrdemAsync(ctx);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Recebida, motivo);
                        Compensacao(ctx, motivo, nameof(LiberarReservaEstoque));
                        await SalvarAsync(ctx);
                    })
                    .Finalize(),

                // A API já cancelou a OS; aqui só se avisa a Execução.
                When(CanceladaManualmente)
                    .ThenAsync(async ctx =>
                    {
                        var motivo = ctx.Message.Motivo ?? "OS cancelada durante o diagnóstico";
                        await PublicarAsync(ctx, new LiberarReservaEstoque { Motivo = motivo });
                        Compensacao(ctx, motivo, nameof(LiberarReservaEstoque));
                        await SalvarAsync(ctx);
                    })
                    .Finalize());

            During(AguardandoOrcamento,
                When(OrcamentoGerado)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        ordem.RegistrarOrcamento(ctx.Message.OrcamentoId);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.AguardandoAprovacao,
                                               $"Orçamento {ctx.Message.OrcamentoId} gerado");

                        ctx.Saga.OrcamentoId    = ctx.Message.OrcamentoId;
                        ctx.Saga.ValorOrcamento = ctx.Message.ValorTotal;
                        ctx.Saga.PrazoAprovacao = ctx.Message.Validade
                                                  ?? Agora(ctx).AddHours(Opcoes(ctx).PrazoAprovacaoHoras);
                        await SalvarAsync(ctx);
                    })
                    .TransitionTo(AguardandoAprovacao)
                    .Then(ctx => Passo(ctx, nameof(OrcamentoGerado))),

                // Sem orçamento não há o que compensar: a OS volta para diagnóstico e a Execução refaz.
                When(OrcamentoFalhou)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.EmDiagnostico, $"Orçamento falhou: {ctx.Message.Motivo}");
                        await SolicitarDiagnosticoAsync(ctx, ordem.DescricaoProblema);
                        await SalvarAsync(ctx);
                    })
                    .TransitionTo(Diagnosticando)
                    .Then(ctx => Passo(ctx, nameof(OrcamentoFalhou))));

            // Regra do negócio: o pagamento é feito com a OS em Aguardando Aprovação. O cliente
            // aprova pagando (o Web chama o Billing); o PagamentoConfirmado é a aprovação.
            During(AguardandoAprovacao,
                When(PagamentoConfirmado)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        ordem.RegistrarPagamento(ctx.Message.PagamentoId);
                        ctx.Saga.PagamentoId    = ctx.Message.PagamentoId;
                        ctx.Saga.PrazoAprovacao = null;
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.EmExecucao,
                                               $"Orçamento aprovado com o pagamento {ctx.Message.PagamentoId}");

                        await PublicarAsync(ctx, new IniciarExecucao
                        {
                            OrcamentoId = ctx.Saga.OrcamentoId ?? 0,
                            Itens       = ordem.Itens.Select(ParaContrato).ToList(),
                        });
                        await SalvarAsync(ctx);
                    })
                    .TransitionTo(Executando)
                    .Then(ctx => Passo(ctx, nameof(PagamentoConfirmado))),

                // Cartão recusado não cancela nada: a OS continua aguardando e o cliente pode
                // tentar de novo até o prazo de aprovação.
                When(PagamentoFalhou)
                    .Then(ctx => ctx.GetServiceOrCreateInstance<ILogger<OrdemServicoStateMachine>>().LogInformation(
                        "Saga {CorrelationId}: pagamento recusado ({Motivo}); a OS segue aguardando aprovação.",
                        ctx.Saga.CorrelationId, ctx.Message.Motivo))
                    .Then(ctx => Passo(ctx, nameof(PagamentoFalhou))),

                // Aviso de aprovação sem pagamento: só registra. A OS sai daqui com o pagamento.
                When(OrcamentoAprovado)
                    .Then(ctx => Passo(ctx, nameof(OrcamentoAprovado))),

                When(OrcamentoRecusado)
                    .ThenAsync(async ctx =>
                    {
                        var motivo = string.IsNullOrWhiteSpace(ctx.Message.Motivo)
                            ? "Orçamento recusado pelo cliente"
                            : $"Orçamento recusado pelo cliente: {ctx.Message.Motivo}";

                        await PublicarAsync(ctx, new LiberarReservaEstoque { Motivo = motivo });
                        var ordem = await ObterOrdemAsync(ctx);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Cancelada, motivo);
                        Compensacao(ctx, motivo, nameof(LiberarReservaEstoque));
                        await SalvarAsync(ctx);
                    })
                    .Finalize(),

                When(PrazoAprovacaoExpirado)
                    .ThenAsync(ctx => IniciarCompensacaoAsync(ctx, "Prazo de aprovação do orçamento expirado",
                                                              cancelarOrcamento: true, estornarPagamento: false, liberarEstoque: true))
                    .TransitionTo(Compensando)
                    .If(ctx => CompensacaoConcluida(ctx.Saga), x => x.Finalize()));

            During(Executando,
                When(ExecucaoIniciada)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        ordem.RegistrarPeriodoExecucao(ctx.Message.DataInicio, null);
                        await SalvarAsync(ctx);
                    })
                    .Then(ctx => Passo(ctx, nameof(ExecucaoIniciada))),

                When(ExecucaoFinalizada)
                    .ThenAsync(async ctx =>
                    {
                        var ordem = await ObterOrdemAsync(ctx);
                        await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Finalizada, "Execução finalizada");
                        ordem.RegistrarPeriodoExecucao(ctx.Message.DataInicio, ctx.Message.DataFim);
                        await SalvarAsync(ctx);
                    })
                    .Then(ctx => Passo(ctx, nameof(ExecucaoFinalizada)))
                    .Finalize(),

                When(ExecucaoFalhou)
                    .ThenAsync(ctx => IniciarCompensacaoAsync(ctx, $"Execução falhou: {ctx.Message.Motivo}",
                                                              cancelarOrcamento: true, estornarPagamento: true, liberarEstoque: false))
                    .TransitionTo(Compensando)
                    .If(ctx => CompensacaoConcluida(ctx.Saga), x => x.Finalize()));

            During(Compensando,
                When(OrcamentoCancelado)
                    .Then(ctx => ctx.Saga.AguardandoOrcamentoCancelado = false)
                    .ThenAsync(ConcluirCompensacaoSeProntaAsync)
                    .If(ctx => CompensacaoConcluida(ctx.Saga), x => x.Finalize()),

                When(PagamentoEstornado)
                    .Then(ctx => ctx.Saga.AguardandoPagamentoEstornado = false)
                    .ThenAsync(ConcluirCompensacaoSeProntaAsync)
                    .If(ctx => CompensacaoConcluida(ctx.Saga), x => x.Finalize()),

                // Pagamento confirmado depois que a OS saiu de Aguardando Aprovação (ex.: o prazo
                // venceu enquanto o cliente pagava): fora da regra, então é estornado.
                When(PagamentoConfirmado, ctx => ctx.Saga.PagamentoId != ctx.Message.PagamentoId)
                    .ThenAsync(EstornarPagamentoForaDoPrazoAsync));

            // Terminada a Saga, a linha do estado é apagada; o caminho completo fica no histórico da OS.
            SetCompletedWhenFinalized();
        }

        // ── Configuração dos eventos ───────────────────────────────────────────

        private void Externo<T>(System.Linq.Expressions.Expression<Func<Event<T>>> evento) where T : MensagemSaga =>
            Event(evento, e =>
            {
                e.CorrelateById(c => c.Message.CorrelationId);
                e.OnMissingInstance(m => m.Discard());
            });

        private void Interno<T>(System.Linq.Expressions.Expression<Func<Event<T>>> evento, Func<T, Guid> id) where T : class =>
            Event(evento, e =>
            {
                e.CorrelateById(c => id(c.Message));
                e.OnMissingInstance(m => m.Discard());
            });

        // ── Passos ─────────────────────────────────────────────────────────────

        private static async Task SolicitarDiagnosticoAsync(BehaviorContext<OrdemServicoSagaState> ctx, string descricaoProblema)
        {
            ctx.Saga.PrazoDiagnostico = Agora(ctx).AddHours(Opcoes(ctx).PrazoDiagnosticoHoras);

            await PublicarAsync(ctx, new IniciarDiagnostico
            {
                ClienteId         = ctx.Saga.ClienteId,
                VeiculoId         = ctx.Saga.VeiculoId,
                DescricaoProblema = descricaoProblema,
            });
        }

        private static async Task IniciarCompensacaoAsync(BehaviorContext<OrdemServicoSagaState> ctx, string motivo,
                                                         bool cancelarOrcamento, bool estornarPagamento, bool liberarEstoque)
        {
            var saga = ctx.Saga;
            var compensacoes = new List<string>();

            saga.MotivoCompensacao = motivo;
            saga.PrazoAprovacao    = null;

            if (estornarPagamento && saga.PagamentoId.HasValue)
            {
                saga.AguardandoPagamentoEstornado = true;
                await PublicarAsync(ctx, new EstornarPagamento { PagamentoId = saga.PagamentoId.Value, Motivo = motivo });
                compensacoes.Add(nameof(EstornarPagamento));
            }

            if (cancelarOrcamento && saga.OrcamentoId.HasValue)
            {
                saga.AguardandoOrcamentoCancelado = true;
                await PublicarAsync(ctx, new CancelarOrcamento { OrcamentoId = saga.OrcamentoId.Value, Motivo = motivo });
                compensacoes.Add(nameof(CancelarOrcamento));
            }

            if (liberarEstoque)
            {
                await PublicarAsync(ctx, new LiberarReservaEstoque { Motivo = motivo });
                compensacoes.Add(nameof(LiberarReservaEstoque));
            }

            var ordem = await ObterOrdemAsync(ctx);
            await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Compensando, motivo);
            Compensacao(ctx, motivo, [.. compensacoes]);

            // Sem confirmação a esperar (nada foi pedido ao Billing), a compensação já termina aqui.
            if (CompensacaoConcluida(saga))
                await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Cancelada, motivo);

            await SalvarAsync(ctx);
        }

        private static async Task EstornarPagamentoForaDoPrazoAsync(BehaviorContext<OrdemServicoSagaState, PagamentoConfirmado> ctx)
        {
            var motivo = $"Pagamento {ctx.Message.PagamentoId} recebido fora de Aguardando Aprovação";
            ctx.Saga.PagamentoId                  = ctx.Message.PagamentoId;
            ctx.Saga.AguardandoPagamentoEstornado = true;

            await PublicarAsync(ctx, new EstornarPagamento { PagamentoId = ctx.Message.PagamentoId, Motivo = motivo });
            Compensacao(ctx, motivo, nameof(EstornarPagamento));
            await SalvarAsync(ctx);
        }

        private static async Task ConcluirCompensacaoSeProntaAsync(BehaviorContext<OrdemServicoSagaState> ctx)
        {
            if (!CompensacaoConcluida(ctx.Saga))
                return;

            var ordem = await ObterOrdemAsync(ctx);
            if (ordem.Status == StatusOrdemServico.Compensando)
                await MudarStatusAsync(ctx, ordem, StatusOrdemServico.Cancelada,
                                       $"Compensação concluída: {ctx.Saga.MotivoCompensacao}");
            await SalvarAsync(ctx);
        }

        private static bool CompensacaoConcluida(OrdemServicoSagaState saga) =>
            !saga.AguardandoOrcamentoCancelado && !saga.AguardandoPagamentoEstornado;

        // ── Apoio ──────────────────────────────────────────────────────────────

        private static async Task<OrdemServico> ObterOrdemAsync(BehaviorContext<OrdemServicoSagaState> ctx) =>
            await ctx.GetServiceOrCreateInstance<IOrdemServicoRepository>().ObterPorIdAsync(ctx.Saga.OrdemServicoId, ctx.CancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Ordem de Serviço com Id '{ctx.Saga.OrdemServicoId}' não encontrada.");

        /// <summary>Muda o status com origem "Saga", registra no New Relic e publica OrdemServicoStatusAlterado.</summary>
        private static async Task MudarStatusAsync(BehaviorContext<OrdemServicoSagaState> ctx, OrdemServico ordem,
                                                   StatusOrdemServico novo, string motivo)
        {
            var (anterior, tempo) = ordem.AlterarStatus(novo, Agora(ctx), OrigemAlteracaoStatus.Saga, motivo,
                                                        ctx.Saga.CorrelationIdRequisicao);
            ctx.GetServiceOrCreateInstance<IMonitoramentoService>().RegistrarMudancaStatus(ordem, anterior, tempo);

            await PublicarAsync(ctx, new OrdemServicoStatusAlterado
            {
                ClienteId      = ordem.ClienteId,
                StatusAnterior = anterior.ToString(),
                StatusAtual    = novo.ToString(),
                Motivo         = motivo,
            });
        }

        /// <summary>
        /// Publica com os campos comuns preenchidos e os cabeçalhos do contrato
        /// (correlation-id e x-correlation-id).
        /// </summary>
        private static Task PublicarAsync<T>(BehaviorContext<OrdemServicoSagaState> ctx, T mensagem) where T : class
        {
            if (mensagem is MensagemSaga comum)
                mensagem = (T)(object)(comum with
                {
                    CorrelationId  = ctx.Saga.CorrelationId,
                    OrdemServicoId = ctx.Saga.OrdemServicoId,
                    DataHora       = Agora(ctx),
                });

            return ctx.Publish(mensagem, p => CabecalhosSaga.Aplicar(p, ctx.Saga.CorrelationId, ctx.Saga.CorrelationIdRequisicao));
        }

        /// <summary>
        /// Grava a OS na mesma transação do estado da Saga e do outbox (mesmo DbContext do escopo).
        /// </summary>
        private static Task SalvarAsync(BehaviorContext<OrdemServicoSagaState> ctx)
        {
            ctx.Saga.DataAtualizacao = Agora(ctx);
            return ctx.GetServiceOrCreateInstance<IOrdemServicoRepository>().SalvarAsync(ctx.CancellationToken);
        }

        private static void Passo(BehaviorContext<OrdemServicoSagaState> ctx, string evento) =>
            ctx.GetServiceOrCreateInstance<IMonitoramentoService>()
               .RegistrarPassoSaga(ctx.Saga.CorrelationId, ctx.Saga.OrdemServicoId, evento, ctx.Saga.EstadoAtual);

        private static void Compensacao(BehaviorContext<OrdemServicoSagaState> ctx, string motivo, params string[] compensacoes) =>
            ctx.GetServiceOrCreateInstance<IMonitoramentoService>()
               .RegistrarCompensacaoSaga(ctx.Saga.CorrelationId, ctx.Saga.OrdemServicoId, motivo, compensacoes);

        private static DateTime Agora(BehaviorContext<OrdemServicoSagaState> ctx) =>
            ctx.GetServiceOrCreateInstance<IRelogio>().UtcAgora;

        private static SagaOptions Opcoes(BehaviorContext<OrdemServicoSagaState> ctx) =>
            ctx.GetServiceOrCreateInstance<IOptions<SagaOptions>>().Value;

        private static (string, TipoItemOrdemServico, int, decimal, int?, int?) ParaDominio(ItemContrato item)
        {
            if (!Enum.TryParse<TipoItemOrdemServico>(item.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo))
                throw new RegraDeNegocioException($"Tipo de item inválido no diagnóstico: '{item.Tipo}'. Use Peca, Insumo ou Servico.");

            return (item.Descricao, tipo, item.Quantidade, item.ValorUnitario, item.EstoqueId, item.ServicoOficinaId);
        }

        private static ItemContrato ParaContrato(Domain.Entities.ItemOrdemServico item) => new()
        {
            Descricao        = item.Descricao,
            Tipo             = item.Tipo.ToString(),
            Quantidade       = item.Quantidade,
            ValorUnitario    = item.ValorUnitario,
            EstoqueId        = item.EstoqueId,
            ServicoOficinaId = item.ServicoOficinaId,
        };
    }
}
