namespace OsService.Application.Saga
{
    // Mensagens internas do OS Service (API e verificador de prazos → Saga). Não fazem parte do
    // contrato com os outros serviços; o exchange delas fica no namespace OsService.Application.Saga.

    /// <summary>A OS foi para diagnóstico com a Saga ligada: começa a Saga.</summary>
    public record DiagnosticoSolicitado
    {
        public Guid CorrelationId { get; init; }
        public int OrdemServicoId { get; init; }
        public int ClienteId { get; init; }
        public int VeiculoId { get; init; }
        public string DescricaoProblema { get; init; } = string.Empty;
        public string NomeCliente { get; init; } = string.Empty;
        public string EmailCliente { get; init; } = string.Empty;
        public string? CorrelationIdRequisicao { get; init; }
    }

    /// <summary>A OS foi cancelada pela API durante o diagnóstico.</summary>
    public record OrdemServicoCanceladaManualmente
    {
        public Guid CorrelationId { get; init; }
        public string? Motivo { get; init; }
    }

    public record PrazoDiagnosticoExpirado
    {
        public Guid CorrelationId { get; init; }
    }

    public record PrazoAprovacaoExpirado
    {
        public Guid CorrelationId { get; init; }
    }
}
