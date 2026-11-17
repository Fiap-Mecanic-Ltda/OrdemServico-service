using OsService.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace OsService.API.ViewModels
{
    public class AbrirOrdemServicoViewModel
    {
        [Required(ErrorMessage = "A descrição do problema é obrigatória.")]
        [MaxLength(1000, ErrorMessage = "A descrição não pode ultrapassar 1000 caracteres.")]
        public string DescricaoProblema { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue, ErrorMessage = "O valor estimado deve ser maior que zero.")]
        public decimal? ValorTotalEstimado { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "O VeiculoId é obrigatório.")]
        public int VeiculoId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "O ClienteId é obrigatório.")]
        public int ClienteId { get; set; }
    }

    public class AtualizarOrdemServicoViewModel
    {
        [Required(ErrorMessage = "A descrição do problema é obrigatória.")]
        [MaxLength(1000, ErrorMessage = "A descrição não pode ultrapassar 1000 caracteres.")]
        public string DescricaoProblema { get; set; } = string.Empty;

        [Range(0.01, double.MaxValue, ErrorMessage = "O valor estimado deve ser maior que zero.")]
        public decimal? ValorTotalEstimado { get; set; }
    }

    public class RecusaOrdemServicoViewModel
    {
        [MaxLength(500, ErrorMessage = "O motivo não pode ultrapassar 500 caracteres.")]
        public string? MotivoRecusa { get; set; }
    }

    public class CancelamentoOrdemServicoViewModel
    {
        [MaxLength(500, ErrorMessage = "O motivo não pode ultrapassar 500 caracteres.")]
        public string? Motivo { get; set; }
    }

    public class ItemOrdemServicoViewModel
    {
        [Required(ErrorMessage = "A descrição do item é obrigatória.")]
        [MaxLength(1000, ErrorMessage = "A descrição do item deve ter no máximo 1000 caracteres.")]
        public string Descricao { get; set; } = string.Empty;

        [EnumDataType(typeof(TipoItemOrdemServico), ErrorMessage = "Tipo inválido. Use Peca, Insumo ou Servico.")]
        public TipoItemOrdemServico Tipo { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "A quantidade deve ser maior que zero.")]
        public int Quantidade { get; set; }

        [Range(0.0, double.MaxValue, ErrorMessage = "O valor unitário não pode ser negativo.")]
        public decimal ValorUnitario { get; set; }

        public int? EstoqueId { get; set; }

        public int? ServicoOficinaId { get; set; }
    }
}
