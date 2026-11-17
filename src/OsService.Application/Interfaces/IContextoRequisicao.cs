namespace OsService.Application.Interfaces
{
    /// <summary>Dados da requisição atual que a aplicação repassa adiante.</summary>
    public interface IContextoRequisicao
    {
        /// <summary>X-Correlation-Id da requisição (gravado no histórico e nos logs).</summary>
        string? CorrelationId { get; }

        /// <summary>Header Authorization recebido, repassado nas chamadas ao MechanicLtda.</summary>
        string? Authorization { get; }
    }
}
