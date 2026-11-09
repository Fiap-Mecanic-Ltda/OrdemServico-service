using OsService.Domain.Enums;

namespace OsService.Domain.Regras
{
    /// <summary>
    /// Máquina de status da OS: quais transições são permitidas a partir de cada status.
    /// Vale tanto para as transições manuais (API, enquanto a Saga está desligada) quanto para
    /// as feitas pela Saga. Ver docs/contratos-eventos.md.
    /// </summary>
    public static class TransicoesDeStatus
    {
        private static readonly Dictionary<StatusOrdemServico, StatusOrdemServico[]> _permitidas = new()
        {
            [StatusOrdemServico.Recebida] =
                [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.Cancelada],

            // AguardandoAprovacao direto: fluxo manual da Fase 3 (o orçamento é feito fora da Saga).
            // Recebida: diagnóstico falhou.
            [StatusOrdemServico.EmDiagnostico] =
                [StatusOrdemServico.AguardandoOrcamento, StatusOrdemServico.AguardandoAprovacao,
                 StatusOrdemServico.Recebida, StatusOrdemServico.Cancelada],

            // EmDiagnostico: orçamento falhou.
            [StatusOrdemServico.AguardandoOrcamento] =
                [StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmDiagnostico,
                 StatusOrdemServico.Cancelada],

            // EmExecucao direto: aprovação no fluxo manual, sem etapa de pagamento.
            [StatusOrdemServico.AguardandoAprovacao] =
                [StatusOrdemServico.AguardandoPagamento, StatusOrdemServico.EmExecucao,
                 StatusOrdemServico.Compensando, StatusOrdemServico.Cancelada],

            [StatusOrdemServico.AguardandoPagamento] =
                [StatusOrdemServico.EmExecucao, StatusOrdemServico.Compensando, StatusOrdemServico.Cancelada],

            [StatusOrdemServico.EmExecucao] =
                [StatusOrdemServico.Finalizada, StatusOrdemServico.Compensando],

            [StatusOrdemServico.Compensando] =
                [StatusOrdemServico.Cancelada],

            [StatusOrdemServico.Finalizada] =
                [StatusOrdemServico.Entregue],

            [StatusOrdemServico.Entregue]  = [],
            [StatusOrdemServico.Cancelada] = [],
        };

        public static bool Permitida(StatusOrdemServico de, StatusOrdemServico para) =>
            _permitidas.TryGetValue(de, out var destinos) && destinos.Contains(para);

        public static IReadOnlyCollection<StatusOrdemServico> DestinosDe(StatusOrdemServico de) =>
            _permitidas.TryGetValue(de, out var destinos) ? destinos : [];
    }
}
