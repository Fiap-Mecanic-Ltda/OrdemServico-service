using OsService.Domain.Entities.Base;
using OsService.Domain.Enums;

namespace OsService.Domain.Entities
{
    /// <summary>
    /// Uma linha por mudança de status. É o que responde "consulta de status e histórico" e
    /// mostra o caminho da Saga, inclusive as compensações.
    /// </summary>
    public class HistoricoStatusOrdemServico : Entity<long>
    {
        public int OrdemServicoId { get; private set; }
        public StatusOrdemServico? StatusAnterior { get; private set; }
        public StatusOrdemServico StatusNovo { get; private set; }
        public DateTime DataAlteracao { get; private set; }
        public OrigemAlteracaoStatus Origem { get; private set; }
        public string? Motivo { get; private set; }
        public string? CorrelationId { get; private set; }

        // EF Core
        private HistoricoStatusOrdemServico() { }

        internal HistoricoStatusOrdemServico(StatusOrdemServico? statusAnterior, StatusOrdemServico statusNovo,
                                             DateTime dataAlteracao, OrigemAlteracaoStatus origem,
                                             string? motivo, string? correlationId)
        {
            StatusAnterior = statusAnterior;
            StatusNovo     = statusNovo;
            DataAlteracao  = dataAlteracao;
            Origem         = origem;
            Motivo         = motivo;
            CorrelationId  = correlationId;
        }
    }
}
