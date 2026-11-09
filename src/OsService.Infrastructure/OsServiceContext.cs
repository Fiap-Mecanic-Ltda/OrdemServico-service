using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities;

namespace OsService.Infrastructure
{
    /// <summary>
    /// Contexto do banco próprio do OS Service (schema "os"). Nenhum outro serviço lê ou grava
    /// aqui. O estado da Saga e o outbox de mensagens entram na etapa da Saga.
    /// </summary>
    public class OsServiceContext : DbContext
    {
        public OsServiceContext(DbContextOptions<OsServiceContext> options) : base(options)
        {
        }

        public DbSet<OrdemServico> OrdensServico => Set<OrdemServico>();
        public DbSet<ItemOrdemServico> ItensOrdemServico => Set<ItemOrdemServico>();
        public DbSet<HistoricoStatusOrdemServico> HistoricoStatus => Set<HistoricoStatusOrdemServico>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("os");
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OsServiceContext).Assembly);
        }
    }
}
