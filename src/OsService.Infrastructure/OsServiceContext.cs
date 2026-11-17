using MassTransit;
using Microsoft.EntityFrameworkCore;
using OsService.Infrastructure.Configuracoes;
using OsService.Domain.Entities;

namespace OsService.Infrastructure
{
    /// <summary>
    /// Contexto do banco próprio do OS Service (schema "os"). Nenhum outro serviço lê ou grava
    /// aqui. Guarda também o estado da Saga e as tabelas de inbox e outbox do MassTransit, para
    /// que status da OS, estado da Saga e mensagens publicadas mudem na mesma transação.
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

            new OrdemServicoSagaStateMap().Configure(modelBuilder);
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }
    }
}
