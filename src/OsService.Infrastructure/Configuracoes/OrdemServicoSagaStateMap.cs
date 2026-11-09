using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OsService.Application.Saga;

namespace OsService.Infrastructure.Configuracoes
{
    public class OrdemServicoSagaStateMap : SagaClassMap<OrdemServicoSagaState>
    {
        protected override void Configure(EntityTypeBuilder<OrdemServicoSagaState> entity, ModelBuilder model)
        {
            entity.ToTable("SagaOrdemServico");

            entity.Property(s => s.EstadoAtual).HasMaxLength(64).IsRequired();
            entity.Property(s => s.NomeCliente).HasMaxLength(200);
            entity.Property(s => s.EmailCliente).HasMaxLength(200);
            entity.Property(s => s.ValorOrcamento).HasPrecision(18, 2);
            entity.Property(s => s.MotivoCompensacao).HasMaxLength(500);
            entity.Property(s => s.CorrelationIdRequisicao).HasMaxLength(128);

            entity.HasIndex(s => s.OrdemServicoId);
            // Consulta do verificador de prazos.
            entity.HasIndex(s => new { s.EstadoAtual, s.PrazoDiagnostico, s.PrazoAprovacao });
        }
    }
}
