using OsService.Domain.Entities.Base;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;

namespace OsService.Domain.Entities
{
    /// <summary>
    /// Item da OS guardado como cópia (snapshot): descrição e valor do momento do diagnóstico.
    /// EstoqueId e ServicoOficinaId apontam para dados da Execução apenas por Id, sem chave
    /// estrangeira, porque cada serviço tem o próprio banco.
    /// </summary>
    public class ItemOrdemServico : Entity<int>
    {
        public int OrdemServicoId { get; private set; }
        public string Descricao { get; private set; } = string.Empty;
        public TipoItemOrdemServico Tipo { get; private set; }
        public int Quantidade { get; private set; }
        public decimal ValorUnitario { get; private set; }
        public decimal ValorTotal { get; private set; }
        public int? EstoqueId { get; private set; }
        public int? ServicoOficinaId { get; private set; }

        // EF Core
        private ItemOrdemServico() { }

        internal ItemOrdemServico(string descricao, TipoItemOrdemServico tipo, int quantidade, decimal valorUnitario,
                                  int? estoqueId, int? servicoOficinaId)
        {
            Atualizar(descricao, tipo, quantidade, valorUnitario, estoqueId, servicoOficinaId);
        }

        internal void Atualizar(string descricao, TipoItemOrdemServico tipo, int quantidade, decimal valorUnitario,
                                int? estoqueId, int? servicoOficinaId)
        {
            if (string.IsNullOrWhiteSpace(descricao))
                throw new RegraDeNegocioException("A descrição do item é obrigatória.");
            if (!Enum.IsDefined(tipo))
                throw new RegraDeNegocioException("Tipo de item inválido.");
            if (quantidade <= 0)
                throw new RegraDeNegocioException("A quantidade do item deve ser maior que zero.");
            if (valorUnitario < 0)
                throw new RegraDeNegocioException("O valor unitário do item não pode ser negativo.");

            Descricao        = descricao.Trim();
            Tipo             = tipo;
            Quantidade       = quantidade;
            ValorUnitario    = valorUnitario;
            ValorTotal       = quantidade * valorUnitario;
            EstoqueId        = estoqueId;
            ServicoOficinaId = servicoOficinaId;
        }
    }
}
