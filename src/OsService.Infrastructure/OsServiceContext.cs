using Microsoft.EntityFrameworkCore;

namespace OsService.Infrastructure
{
    /// <summary>
    /// Contexto do banco próprio do OS Service. Nenhum outro serviço lê ou grava aqui.
    /// As entidades (OS, itens, histórico e estado da Saga) entram nas próximas etapas.
    /// </summary>
    public class OsServiceContext : DbContext
    {
        public OsServiceContext(DbContextOptions<OsServiceContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("os");
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OsServiceContext).Assembly);
        }
    }
}
