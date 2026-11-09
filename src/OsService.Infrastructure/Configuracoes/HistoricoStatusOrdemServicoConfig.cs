using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OsService.Domain.Entities;

namespace OsService.Infrastructure.Configuracoes
{
    public class HistoricoStatusOrdemServicoConfig : IEntityTypeConfiguration<HistoricoStatusOrdemServico>
    {
        public void Configure(EntityTypeBuilder<HistoricoStatusOrdemServico> builder)
        {
            builder.ToTable("HistoricoStatusOrdemServico");
            builder.HasKey(h => h.Id);

            builder.Property(h => h.StatusNovo).IsRequired();
            builder.Property(h => h.Origem).IsRequired().HasConversion<string>().HasMaxLength(20);
            builder.Property(h => h.Motivo).HasMaxLength(500);
            builder.Property(h => h.CorrelationId).HasMaxLength(128);

            builder.HasIndex(h => new { h.OrdemServicoId, h.DataAlteracao });
        }
    }
}
