using MassTransit;

namespace OsService.Application.Saga
{
    /// <summary>Cabeçalhos que o contrato exige em toda mensagem publicada.</summary>
    public static class CabecalhosSaga
    {
        public const string CorrelationId = "correlation-id";
        public const string CorrelationIdRequisicao = "x-correlation-id";

        public static void Aplicar(SendContext contexto, Guid correlationId, string? correlationIdRequisicao)
        {
            contexto.CorrelationId = correlationId;
            contexto.Headers.Set(CorrelationId, correlationId.ToString());
            if (!string.IsNullOrWhiteSpace(correlationIdRequisicao))
                contexto.Headers.Set(CorrelationIdRequisicao, correlationIdRequisicao);
        }
    }
}
