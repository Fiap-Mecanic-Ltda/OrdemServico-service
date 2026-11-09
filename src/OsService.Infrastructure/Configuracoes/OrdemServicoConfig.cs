using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OsService.Domain.Entities;

namespace OsService.Infrastructure.Configuracoes
{
    public class OrdemServicoConfig : IEntityTypeConfiguration<OrdemServico>
    {
        public void Configure(EntityTypeBuilder<OrdemServico> builder)
        {
            builder.ToTable("OrdemServico");
            builder.HasKey(os => os.Id);

            builder.Property(os => os.CorrelationId).IsRequired();
            builder.HasIndex(os => os.CorrelationId).IsUnique();

            builder.Property(os => os.DescricaoProblema).IsRequired().HasMaxLength(OrdemServico.TamanhoMaximoDescricao);
            builder.Property(os => os.ValorTotalEstimado).HasPrecision(18, 2);
            builder.Property(os => os.Status).IsRequired();

            builder.HasIndex(os => os.ClienteId);
            builder.HasIndex(os => os.Status);

            builder.Ignore(os => os.ValorTotalItens);
            builder.Ignore(os => os.TempoExecucao);

            builder.HasMany(os => os.Itens)
                   .WithOne()
                   .HasForeignKey(i => i.OrdemServicoId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(os => os.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.HasMany(os => os.Historico)
                   .WithOne()
                   .HasForeignKey(h => h.OrdemServicoId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Navigation(os => os.Historico).UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
