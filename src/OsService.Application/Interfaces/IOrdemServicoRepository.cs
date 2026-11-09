using OsService.Domain.Entities;

namespace OsService.Application.Interfaces
{
    public interface IOrdemServicoRepository
    {
        /// <summary>OS com itens e histórico.</summary>
        Task<OrdemServico?> ObterPorIdAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<OrdemServico>> ObterTodasAsync(CancellationToken ct = default);
        Task<IReadOnlyList<OrdemServico>> ObterPorClienteAsync(int clienteId, CancellationToken ct = default);
        Task<(int Quantidade, double MediaMinutos)> ObterTempoMedioExecucaoAsync(CancellationToken ct = default);
        Task AdicionarAsync(OrdemServico ordemServico, CancellationToken ct = default);
        void Remover(OrdemServico ordemServico);
        Task SalvarAsync(CancellationToken ct = default);
    }
}
