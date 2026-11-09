using OsService.Domain.Entities.Base;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;
using OsService.Domain.Regras;

namespace OsService.Domain.Entities
{
    /// <summary>
    /// Ordem de Serviço. Cliente e veículo pertencem a outro serviço e ficam aqui só por Id.
    /// Toda mudança de status passa por <see cref="AlterarStatus"/>, que valida a transição
    /// (<see cref="TransicoesDeStatus"/>) e grava uma linha no histórico.
    /// </summary>
    public class OrdemServico : Entity<int>
    {
        public const int TamanhoMaximoDescricao = 1000;

        private readonly List<ItemOrdemServico> _itens = [];
        private readonly List<HistoricoStatusOrdemServico> _historico = [];

        /// <summary>Id da Saga desta OS; vai em todas as mensagens trocadas com os outros serviços.</summary>
        public Guid CorrelationId { get; private set; }
        public int ClienteId { get; private set; }
        public int VeiculoId { get; private set; }
        public string DescricaoProblema { get; private set; } = string.Empty;
        public decimal? ValorTotalEstimado { get; private set; }
        public StatusOrdemServico Status { get; private set; }
        public DateTime DataCriacao { get; private set; }
        public DateTime? DataModificacao { get; private set; }
        public DateTime DataAlteracaoStatus { get; private set; }
        public DateTime? DataInicioExecucao { get; private set; }
        public DateTime? DataFimExecucao { get; private set; }

        /// <summary>Referências a dados do serviço de Orçamento e Pagamento (sem chave estrangeira).</summary>
        public int? OrcamentoId { get; private set; }
        public int? PagamentoId { get; private set; }

        public IReadOnlyCollection<ItemOrdemServico> Itens => _itens.AsReadOnly();
        public IReadOnlyCollection<HistoricoStatusOrdemServico> Historico => _historico.AsReadOnly();

        public decimal ValorTotalItens => _itens.Sum(i => i.ValorTotal);

        public TimeSpan? TempoExecucao =>
            DataInicioExecucao.HasValue && DataFimExecucao.HasValue
                ? DataFimExecucao.Value - DataInicioExecucao.Value
                : null;

        // EF Core
        private OrdemServico() { }

        public static OrdemServico Abrir(int clienteId, int veiculoId, string descricaoProblema,
                                         decimal? valorTotalEstimado, DateTime agora, string? correlationIdRequisicao)
        {
            if (clienteId <= 0)
                throw new RegraDeNegocioException("ClienteId inválido.");
            if (veiculoId <= 0)
                throw new RegraDeNegocioException("VeiculoId inválido.");

            var ordem = new OrdemServico
            {
                CorrelationId       = Guid.NewGuid(),
                ClienteId           = clienteId,
                VeiculoId           = veiculoId,
                Status              = StatusOrdemServico.Recebida,
                DataCriacao         = agora,
                DataAlteracaoStatus = agora,
            };

            ordem.DefinirDescricao(descricaoProblema, valorTotalEstimado);
            ordem._historico.Add(new HistoricoStatusOrdemServico(null, StatusOrdemServico.Recebida, agora,
                OrigemAlteracaoStatus.Usuario, "Abertura da OS", correlationIdRequisicao));

            return ordem;
        }

        /// <summary>Atualiza descrição e valor estimado. Só enquanto a OS não foi para orçamento.</summary>
        public void AtualizarDados(string descricaoProblema, decimal? valorTotalEstimado, DateTime agora)
        {
            if (Status is not (StatusOrdemServico.Recebida or StatusOrdemServico.EmDiagnostico))
                throw new RegraDeNegocioException(
                    $"A OS só pode ser editada enquanto estiver 'Recebida' ou em 'Diagnóstico'. Status atual: {Status.Descricao()}.");

            DefinirDescricao(descricaoProblema, valorTotalEstimado);
            DataModificacao = agora;
        }

        /// <summary>
        /// Muda o status, validando a transição e registrando o histórico. Retorna o status
        /// anterior e quanto tempo a OS ficou nele (para o monitoramento).
        /// </summary>
        public (StatusOrdemServico StatusAnterior, TimeSpan TempoNoStatusAnterior) AlterarStatus(
            StatusOrdemServico novoStatus, DateTime agora, OrigemAlteracaoStatus origem,
            string? motivo = null, string? correlationIdRequisicao = null)
        {
            if (!TransicoesDeStatus.Permitida(Status, novoStatus))
                throw new RegraDeNegocioException(
                    $"Transição de '{Status.Descricao()}' para '{novoStatus.Descricao()}' não é permitida.");

            var anterior = Status;
            var tempo    = agora - DataAlteracaoStatus;

            Status              = novoStatus;
            DataAlteracaoStatus = agora;
            DataModificacao     = agora;

            if (novoStatus == StatusOrdemServico.EmExecucao && DataInicioExecucao is null)
                DataInicioExecucao = agora;
            if (novoStatus == StatusOrdemServico.Finalizada)
                DataFimExecucao = agora;

            _historico.Add(new HistoricoStatusOrdemServico(anterior, novoStatus, agora, origem,
                Truncar(motivo, 500), correlationIdRequisicao));

            return (anterior, tempo < TimeSpan.Zero ? TimeSpan.Zero : tempo);
        }

        public void RegistrarOrcamento(int orcamentoId) => OrcamentoId = orcamentoId;

        public void RegistrarPagamento(int pagamentoId) => PagamentoId = pagamentoId;

        /// <summary>
        /// Usa as datas informadas pela Execução (quem de fato executa o reparo) no lugar das
        /// datas em que o OS Service processou as mudanças de status.
        /// </summary>
        public void RegistrarPeriodoExecucao(DateTime inicio, DateTime? fim)
        {
            if (fim.HasValue && fim.Value < inicio)
                throw new RegraDeNegocioException("A data de fim da execução não pode ser anterior à de início.");

            DataInicioExecucao = inicio;
            if (fim.HasValue)
                DataFimExecucao = fim;
        }

        public ItemOrdemServico AdicionarItem(string descricao, TipoItemOrdemServico tipo, int quantidade,
                                              decimal valorUnitario, int? estoqueId, int? servicoOficinaId, DateTime agora)
        {
            GarantirItensEditaveis();

            var item = new ItemOrdemServico(descricao, tipo, quantidade, valorUnitario, estoqueId, servicoOficinaId);
            _itens.Add(item);
            DataModificacao = agora;

            return item;
        }

        public ItemOrdemServico AtualizarItem(int itemId, string descricao, TipoItemOrdemServico tipo, int quantidade,
                                              decimal valorUnitario, int? estoqueId, int? servicoOficinaId, DateTime agora)
        {
            GarantirItensEditaveis();

            var item = ObterItem(itemId);
            item.Atualizar(descricao, tipo, quantidade, valorUnitario, estoqueId, servicoOficinaId);
            DataModificacao = agora;

            return item;
        }

        public void RemoverItem(int itemId, DateTime agora)
        {
            GarantirItensEditaveis();

            _itens.Remove(ObterItem(itemId));
            DataModificacao = agora;
        }

        /// <summary>Troca todos os itens (resultado do diagnóstico vindo da Execução).</summary>
        public void SubstituirItens(IEnumerable<(string Descricao, TipoItemOrdemServico Tipo, int Quantidade,
                                                 decimal ValorUnitario, int? EstoqueId, int? ServicoOficinaId)> itens,
                                    DateTime agora)
        {
            GarantirItensEditaveis();

            _itens.Clear();
            foreach (var i in itens)
                _itens.Add(new ItemOrdemServico(i.Descricao, i.Tipo, i.Quantidade, i.ValorUnitario, i.EstoqueId, i.ServicoOficinaId));

            DataModificacao = agora;
        }

        public ItemOrdemServico ObterItem(int itemId) =>
            _itens.FirstOrDefault(i => i.Id == itemId)
            ?? throw new RecursoNaoEncontradoException($"Item '{itemId}' não encontrado na OS {Id}.");

        private void GarantirItensEditaveis()
        {
            if (Status is not (StatusOrdemServico.Recebida or StatusOrdemServico.EmDiagnostico))
                throw new RegraDeNegocioException(
                    $"Os itens só podem ser alterados enquanto a OS estiver 'Recebida' ou em 'Diagnóstico'. Status atual: {Status.Descricao()}.");
        }

        private void DefinirDescricao(string descricaoProblema, decimal? valorTotalEstimado)
        {
            if (string.IsNullOrWhiteSpace(descricaoProblema))
                throw new RegraDeNegocioException("A descrição do problema é obrigatória.");
            if (descricaoProblema.Length > TamanhoMaximoDescricao)
                throw new RegraDeNegocioException($"A descrição do problema tem no máximo {TamanhoMaximoDescricao} caracteres.");
            if (valorTotalEstimado < 0)
                throw new RegraDeNegocioException("O valor estimado não pode ser negativo.");

            DescricaoProblema  = descricaoProblema.Trim();
            ValorTotalEstimado = valorTotalEstimado;
        }

        private static string? Truncar(string? texto, int tamanho) =>
            texto is null || texto.Length <= tamanho ? texto : texto[..tamanho];
    }
}
