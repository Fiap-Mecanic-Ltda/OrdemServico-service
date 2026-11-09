using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OsService.Domain.Entities;

namespace OsService.Infrastructure.Configuracoes
{
    public class ItemOrdemServicoConfig : IEntityTypeConfiguration<ItemOrdemServico>
    {
        public void Configure(EntityTypeBuilder<ItemOrdemServico> builder)
        {
            builder.ToTable("ItemOrdemServico");
            builder.HasKey(i => i.Id);

            builder.Property(i => i.Descricao).IsRequired().HasMaxLength(1000);
            builder.Property(i => i.Tipo).IsRequired().HasConversion<string>().HasMaxLength(20);
            builder.Property(i => i.ValorUnitario).HasPrecision(18, 2);
            builder.Property(i => i.ValorTotal).HasPrecision(18, 2);
        }
    }
}
