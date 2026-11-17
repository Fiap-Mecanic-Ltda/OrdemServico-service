using Microsoft.EntityFrameworkCore;
using OsService.Application.Interfaces;
using OsService.Domain.Entities;
using OsService.Domain.Enums;

namespace OsService.Infrastructure.Repositories
{
    public class OrdemServicoRepository : IOrdemServicoRepository
    {
        private readonly OsServiceContext _context;

        public OrdemServicoRepository(OsServiceContext context) => _context = context;

        public Task<OrdemServico?> ObterPorIdAsync(int id, CancellationToken ct = default) =>
            _context.OrdensServico
                .Include(os => os.Itens)
                .Include(os => os.Historico)
                .AsSplitQuery()
                .FirstOrDefaultAsync(os => os.Id == id, ct);

        public async Task<IReadOnlyList<OrdemServico>> ObterTodasAsync(CancellationToken ct = default) =>
            await _context.OrdensServico
                .Include(os => os.Itens)
                .AsNoTracking()
                .ToListAsync(ct);

        public async Task<IReadOnlyList<OrdemServico>> ObterPorClienteAsync(int clienteId, CancellationToken ct = default) =>
            await _context.OrdensServico
                .Include(os => os.Itens)
                .Where(os => os.ClienteId == clienteId)
                .OrderByDescending(os => os.DataCriacao)
                .AsNoTracking()
                .ToListAsync(ct);

        public async Task<(int Quantidade, double MediaMinutos)> ObterTempoMedioExecucaoAsync(CancellationToken ct = default)
        {
            // Só as datas sobem do banco; a média é calculada aqui para funcionar igual em
            // SQL Server e no provedor em memória dos testes.
            var periodos = await _context.OrdensServico
                .Where(os => (os.Status == StatusOrdemServico.Finalizada || os.Status == StatusOrdemServico.Entregue)
                             && os.DataInicioExecucao != null && os.DataFimExecucao != null)
                .Select(os => new { Inicio = os.DataInicioExecucao!.Value, Fim = os.DataFimExecucao!.Value })
                .ToListAsync(ct);

            if (periodos.Count == 0)
                return (0, 0);

            return (periodos.Count, periodos.Average(p => (p.Fim - p.Inicio).TotalMinutes));
        }

        public async Task AdicionarAsync(OrdemServico ordemServico, CancellationToken ct = default) =>
            await _context.OrdensServico.AddAsync(ordemServico, ct);

        public void Remover(OrdemServico ordemServico) => _context.OrdensServico.Remove(ordemServico);

        public Task SalvarAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
    }
}
