using OsService.Domain.Entities;
using OsService.Domain.Enums;

namespace OsService.Application.DTOs
{
    public record AbrirOrdemServicoDto(int ClienteId, int VeiculoId, string DescricaoProblema, decimal? ValorTotalEstimado);

    public record AtualizarOrdemServicoDto(string DescricaoProblema, decimal? ValorTotalEstimado);

    public record ItemOrdemServicoInputDto(string Descricao, TipoItemOrdemServico Tipo, int Quantidade,
                                           decimal ValorUnitario, int? EstoqueId, int? ServicoOficinaId);

    public record ItemOrdemServicoDto(int Id, string Descricao, TipoItemOrdemServico Tipo, int Quantidade,
                                      decimal ValorUnitario, decimal ValorTotal, int? EstoqueId, int? ServicoOficinaId)
    {
        public static ItemOrdemServicoDto De(ItemOrdemServico i) =>
            new(i.Id, i.Descricao, i.Tipo, i.Quantidade, i.ValorUnitario, i.ValorTotal, i.EstoqueId, i.ServicoOficinaId);
    }

    public record HistoricoStatusDto(StatusOrdemServico? StatusAnterior, string? StatusAnteriorDescricao,
                                     StatusOrdemServico StatusNovo, string StatusNovoDescricao,
                                     DateTime DataAlteracao, OrigemAlteracaoStatus Origem, string? Motivo)
    {
        public static HistoricoStatusDto De(HistoricoStatusOrdemServico h) =>
            new(h.StatusAnterior, h.StatusAnterior?.Descricao(), h.StatusNovo, h.StatusNovo.Descricao(),
                h.DataAlteracao, h.Origem, h.Motivo);
    }

    /// <summary>Mesmos campos da OrdemServicoDto da Fase 3, mais itens, saga e referências externas.</summary>
    public record OrdemServicoDto(
        int Id, Guid CorrelationId, DateTime DataCriacao, DateTime? DataModificacao,
        DateTime? DataInicioExecucao, DateTime? DataFimExecucao, double? TempoExecucaoMinutos,
        StatusOrdemServico Status, string StatusDescricao, string DescricaoProblema,
        decimal? ValorTotalEstimado, decimal ValorTotalItens, int VeiculoId, int ClienteId,
        int? OrcamentoId, int? PagamentoId, IReadOnlyList<ItemOrdemServicoDto> Itens)
    {
        public static OrdemServicoDto De(OrdemServico os) =>
            new(os.Id, os.CorrelationId, os.DataCriacao, os.DataModificacao, os.DataInicioExecucao, os.DataFimExecucao,
                os.TempoExecucao.HasValue ? Math.Round(os.TempoExecucao.Value.TotalMinutes, 2) : null,
                os.Status, os.Status.Descricao(), os.DescricaoProblema, os.ValorTotalEstimado, os.ValorTotalItens,
                os.VeiculoId, os.ClienteId, os.OrcamentoId, os.PagamentoId,
                os.Itens.Select(ItemOrdemServicoDto.De).ToList());
    }

    public record TempoMedioExecucaoDto(int QuantidadeOrdensFinalizadas, double TempoMedioMinutos, double TempoMedioHoras);
}
