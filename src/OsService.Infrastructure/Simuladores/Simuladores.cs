using MassTransit;
using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace OsService.Infrastructure.Simuladores
{
    // Consumidores que fazem o papel da Execução e do Billing para rodar a Saga ponta a ponta
    // sem os outros serviços. Só são registrados com Simuladores:Habilitados=true (ambiente
    // local). Nunca ligar num ambiente onde os serviços reais também consomem: os dois
    // responderiam aos mesmos comandos.
    //
    // O cenário vem de marcadores na descrição do problema da OS:
    //   [falha-diagnostico]  → DiagnosticoFalhou
    //   [recusar]            → OrcamentoRecusado
    //   [falha-pagamento]    → PagamentoFalhou na primeira tentativa e PagamentoConfirmado na segunda
    //   [falha-execucao]     → ExecucaoFalhou
    // Sem marcador, o fluxo vai até Finalizada.

    public static class Marcadores
    {
        public const string FalhaDiagnostico = "[falha-diagnostico]";
        public const string Recusar          = "[recusar]";
        public const string FalhaPagamento   = "[falha-pagamento]";
        public const string FalhaExecucao    = "[falha-execucao]";

        public static bool Contem(IEnumerable<ItemOrdemServico> itens, string marcador) =>
            itens.Any(i => i.Descricao.Contains(marcador, StringComparison.OrdinalIgnoreCase));

        /// <summary>Copia CorrelationId e OrdemServicoId da mensagem recebida.</summary>
        public static T Resposta<T>(MensagemSaga origem, T resposta) where T : MensagemSaga =>
            resposta with { CorrelationId = origem.CorrelationId, OrdemServicoId = origem.OrdemServicoId, DataHora = DateTime.UtcNow };
    }

    /// <summary>O que o Billing simulado precisa lembrar entre um comando e outro.</summary>
    public class MemoriaSimuladores
    {
        public ConcurrentDictionary<int, IReadOnlyList<ItemOrdemServico>> ItensPorOrdem { get; } = new();
    }

    public class SimuladorExecucao : IConsumer<IniciarDiagnostico>, IConsumer<IniciarExecucao>
    {
        private readonly ILogger<SimuladorExecucao> _logger;

        public SimuladorExecucao(ILogger<SimuladorExecucao> logger) => _logger = logger;

        public async Task Consume(ConsumeContext<IniciarDiagnostico> context)
        {
            var m = context.Message;
            _logger.LogInformation("[Simulador Execução] diagnosticando a OS {OrdemServicoId}.", m.OrdemServicoId);

            if (m.DescricaoProblema.Contains(Marcadores.FalhaDiagnostico, StringComparison.OrdinalIgnoreCase))
            {
                await context.Publish(Marcadores.Resposta(m, new DiagnosticoFalhou { Motivo = "Falha simulada no diagnóstico" }));
                return;
            }

            var problema = m.DescricaoProblema.Length > 900 ? m.DescricaoProblema[..900] : m.DescricaoProblema;
            await context.Publish(Marcadores.Resposta(m, new DiagnosticoConcluido
            {
                Itens =
                [
                    new ItemOrdemServico { Descricao = $"Peça para: {problema}", Tipo = "Peca", Quantidade = 1, ValorUnitario = 150m, EstoqueId = 1 },
                    new ItemOrdemServico { Descricao = $"Mão de obra: {problema}", Tipo = "Servico", Quantidade = 1, ValorUnitario = 200m, ServicoOficinaId = 1 },
                ],
                Observacao = "Diagnóstico simulado",
            }));
        }

        public async Task Consume(ConsumeContext<IniciarExecucao> context)
        {
            var m = context.Message;
            _logger.LogInformation("[Simulador Execução] executando a OS {OrdemServicoId}.", m.OrdemServicoId);

            if (Marcadores.Contem(m.Itens, Marcadores.FalhaExecucao))
            {
                await context.Publish(Marcadores.Resposta(m, new ExecucaoFalhou { Motivo = "Falha simulada na execução" }));
                return;
            }

            var inicio = DateTime.UtcNow;
            await context.Publish(Marcadores.Resposta(m, new ExecucaoIniciada { DataInicio = inicio }));
            await context.Publish(Marcadores.Resposta(m, new ExecucaoFinalizada { DataInicio = inicio, DataFim = inicio.AddSeconds(1) }));
        }
    }

    public class SimuladorBilling :
        IConsumer<GerarOrcamento>, IConsumer<CancelarOrcamento>,
        IConsumer<EstornarPagamento>, IConsumer<OrdemServicoStatusAlterado>
    {
        private readonly MemoriaSimuladores _memoria;
        private readonly ILogger<SimuladorBilling> _logger;

        public SimuladorBilling(MemoriaSimuladores memoria, ILogger<SimuladorBilling> logger)
        {
            _memoria = memoria;
            _logger  = logger;
        }

        // Ids determinísticos para facilitar a conferência no histórico.
        private static int OrcamentoId(int ordemServicoId) => 1000 + ordemServicoId;
        private static int PagamentoId(int ordemServicoId) => 2000 + ordemServicoId;

        public Task Consume(ConsumeContext<GerarOrcamento> context)
        {
            var m = context.Message;
            _memoria.ItensPorOrdem[m.OrdemServicoId] = m.Itens;
            _logger.LogInformation("[Simulador Billing] orçamento da OS {OrdemServicoId} para {Email}.", m.OrdemServicoId, m.EmailCliente);

            return context.Publish(Marcadores.Resposta(m, new OrcamentoGerado
            {
                OrcamentoId = OrcamentoId(m.OrdemServicoId),
                ValorTotal  = m.Itens.Sum(i => i.Quantidade * i.ValorUnitario),
                Validade    = DateTime.UtcNow.AddDays(3),
            }));
        }

        /// <summary>
        /// O "cliente" responde quando a OS chega em Aguardando Aprovação: é nesse status que ele
        /// paga (ou recusa). Esperar esse evento, e não responder junto com o OrcamentoGerado,
        /// garante a ordem das mensagens.
        /// </summary>
        public async Task Consume(ConsumeContext<OrdemServicoStatusAlterado> context)
        {
            var m = context.Message;
            if (m.StatusAtual != "AguardandoAprovacao" || !_memoria.ItensPorOrdem.TryGetValue(m.OrdemServicoId, out var itens))
                return;

            if (Marcadores.Contem(itens, Marcadores.Recusar))
            {
                await context.Publish(Marcadores.Resposta(m, new OrcamentoRecusado { OrcamentoId = OrcamentoId(m.OrdemServicoId), Motivo = "Recusa simulada" }));
                return;
            }

            // Cartão recusado na primeira tentativa; o cliente paga de novo e a segunda passa.
            if (Marcadores.Contem(itens, Marcadores.FalhaPagamento))
                await context.Publish(Marcadores.Resposta(m, new PagamentoFalhou { Motivo = "Pagamento recusado pela operadora (simulado)" }));

            await context.Publish(Marcadores.Resposta(m, new PagamentoConfirmado
            {
                PagamentoId   = PagamentoId(m.OrdemServicoId),
                Valor         = itens.Sum(i => i.Quantidade * i.ValorUnitario),
                MeioPagamento = "Cartao",
            }));
        }

        public Task Consume(ConsumeContext<CancelarOrcamento> context) =>
            context.Publish(Marcadores.Resposta(context.Message, new OrcamentoCancelado { OrcamentoId = context.Message.OrcamentoId }));

        public Task Consume(ConsumeContext<EstornarPagamento> context) =>
            context.Publish(Marcadores.Resposta(context.Message, new PagamentoEstornado { PagamentoId = context.Message.PagamentoId }));
    }
}
