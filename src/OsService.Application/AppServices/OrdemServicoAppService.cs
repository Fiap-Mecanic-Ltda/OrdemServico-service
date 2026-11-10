using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsService.Application.DTOs;
using OsService.Application.Exceptions;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Application.Saga;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;

namespace OsService.Application.AppServices
{
    public interface IOrdemServicoAppService
    {
        Task<OrdemServicoDto> AbrirAsync(AbrirOrdemServicoDto dto, CancellationToken ct = default);
        Task<OrdemServicoDto> AtualizarAsync(int id, AtualizarOrdemServicoDto dto, CancellationToken ct = default);
        Task<OrdemServicoDto> ObterAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<OrdemServicoDto>> ListarEmAndamentoAsync(CancellationToken ct = default);
        Task<IReadOnlyList<OrdemServicoDto>> ListarPorClienteAsync(int clienteId, CancellationToken ct = default);
        Task<IReadOnlyList<HistoricoStatusDto>> ObterHistoricoAsync(int id, CancellationToken ct = default);
        Task<TempoMedioExecucaoDto> ObterTempoMedioExecucaoAsync(CancellationToken ct = default);
        Task RemoverAsync(int id, CancellationToken ct = default);

        Task<OrdemServicoDto> IniciarDiagnosticoAsync(int id, CancellationToken ct = default);
        Task<OrdemServicoDto> AguardarAprovacaoAsync(int id, CancellationToken ct = default);
        Task<OrdemServicoDto> AprovarAsync(int id, CancellationToken ct = default);
        Task<OrdemServicoDto> RecusarAsync(int id, string? motivo, CancellationToken ct = default);
        Task<OrdemServicoDto> FinalizarAsync(int id, CancellationToken ct = default);
        Task<OrdemServicoDto> EntregarAsync(int id, CancellationToken ct = default);
        Task<OrdemServicoDto> CancelarAsync(int id, string? motivo, CancellationToken ct = default);

        Task<IReadOnlyList<ItemOrdemServicoDto>> ListarItensAsync(int ordemServicoId, CancellationToken ct = default);
        Task<ItemOrdemServicoDto> ObterItemAsync(int ordemServicoId, int itemId, CancellationToken ct = default);
        Task<ItemOrdemServicoDto> AdicionarItemAsync(int ordemServicoId, ItemOrdemServicoInputDto dto, CancellationToken ct = default);
        Task<ItemOrdemServicoDto> AtualizarItemAsync(int ordemServicoId, int itemId, ItemOrdemServicoInputDto dto, CancellationToken ct = default);
        Task RemoverItemAsync(int ordemServicoId, int itemId, CancellationToken ct = default);
    }

    public class OrdemServicoAppService : IOrdemServicoAppService
    {
        // Ordem da listagem geral: OS mais urgentes primeiro (não é a ordem numérica do enum).
        private static readonly Dictionary<StatusOrdemServico, int> _prioridadeListagem = new()
        {
            [StatusOrdemServico.EmExecucao]          = 1,
            [StatusOrdemServico.AguardandoAprovacao] = 2,
            [StatusOrdemServico.AguardandoOrcamento] = 3,
            [StatusOrdemServico.EmDiagnostico]       = 4,
            [StatusOrdemServico.Compensando]         = 5,
            [StatusOrdemServico.Recebida]            = 6,
        };

        private readonly IOrdemServicoRepository _repositorio;
        private readonly IClienteVeiculoGateway _clienteVeiculo;
        private readonly IMonitoramentoService _monitoramento;
        private readonly IContextoRequisicao _contexto;
        private readonly IRelogio _relogio;
        private readonly IPublicadorMensagens _publicador;
        private readonly SagaOptions _saga;
        private readonly ILogger<OrdemServicoAppService> _logger;

        public OrdemServicoAppService(IOrdemServicoRepository repositorio, IClienteVeiculoGateway clienteVeiculo,
                                      IMonitoramentoService monitoramento, IContextoRequisicao contexto,
                                      IRelogio relogio, IPublicadorMensagens publicador,
                                      IOptions<SagaOptions> saga, ILogger<OrdemServicoAppService> logger)
        {
            _repositorio    = repositorio;
            _clienteVeiculo = clienteVeiculo;
            _monitoramento  = monitoramento;
            _contexto       = contexto;
            _relogio        = relogio;
            _publicador     = publicador;
            _saga           = saga.Value;
            _logger         = logger;
        }

        // ── Abertura e dados da OS ─────────────────────────────────────────────

        public async Task<OrdemServicoDto> AbrirAsync(AbrirOrdemServicoDto dto, CancellationToken ct = default)
        {
            var veiculo = await _clienteVeiculo.ObterVeiculoAsync(dto.VeiculoId, ct)
                ?? throw new RecursoNaoEncontradoException($"Veículo com Id '{dto.VeiculoId}' não encontrado.");

            if (veiculo.ClienteId != dto.ClienteId)
                throw new RegraDeNegocioException("O veículo informado não pertence ao cliente indicado.");

            var ordem = OrdemServico.Abrir(dto.ClienteId, dto.VeiculoId, dto.DescricaoProblema,
                                           dto.ValorTotalEstimado, _relogio.UtcAgora, _contexto.CorrelationId);

            await _repositorio.AdicionarAsync(ordem, ct);
            await SalvarAsync(ct);

            _monitoramento.RegistrarOrdemServicoCriada(ordem);
            _logger.LogInformation("OS {OrdemServicoId} aberta para o cliente {ClienteId} (saga {CorrelationId}).",
                                   ordem.Id, ordem.ClienteId, ordem.CorrelationId);

            return OrdemServicoDto.De(ordem);
        }

        public async Task<OrdemServicoDto> AtualizarAsync(int id, AtualizarOrdemServicoDto dto, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            ordem.AtualizarDados(dto.DescricaoProblema, dto.ValorTotalEstimado, _relogio.UtcAgora);

            // Mesmo gatilho da Fase 3: com descrição e valor estimado, a OS recebida vai para diagnóstico.
            if (ordem.Status == StatusOrdemServico.Recebida && dto.ValorTotalEstimado.HasValue)
                await IrParaDiagnosticoAsync(ordem, "Valor estimado informado", ct);

            await SalvarAsync(ct);
            return OrdemServicoDto.De(ordem);
        }

        public async Task<OrdemServicoDto> ObterAsync(int id, CancellationToken ct = default) =>
            OrdemServicoDto.De(await ObterOrdemAsync(id, ct));

        public async Task<IReadOnlyList<OrdemServicoDto>> ListarEmAndamentoAsync(CancellationToken ct = default)
        {
            var todas = await _repositorio.ObterTodasAsync(ct);

            // Finalizadas, entregues e canceladas saem da listagem geral (continuam no banco e no GET por id).
            return todas
                .Where(os => os.Status is not (StatusOrdemServico.Finalizada or StatusOrdemServico.Entregue or StatusOrdemServico.Cancelada))
                .OrderBy(os => _prioridadeListagem.GetValueOrDefault(os.Status, int.MaxValue))
                .ThenBy(os => os.DataCriacao)
                .Select(OrdemServicoDto.De)
                .ToList();
        }

        public async Task<IReadOnlyList<OrdemServicoDto>> ListarPorClienteAsync(int clienteId, CancellationToken ct = default) =>
            (await _repositorio.ObterPorClienteAsync(clienteId, ct)).Select(OrdemServicoDto.De).ToList();

        public async Task<IReadOnlyList<HistoricoStatusDto>> ObterHistoricoAsync(int id, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            return ordem.Historico
                .OrderBy(h => h.DataAlteracao)
                .ThenBy(h => h.Id)
                .Select(HistoricoStatusDto.De)
                .ToList();
        }

        public async Task<TempoMedioExecucaoDto> ObterTempoMedioExecucaoAsync(CancellationToken ct = default)
        {
            var (quantidade, mediaMinutos) = await _repositorio.ObterTempoMedioExecucaoAsync(ct);

            return new TempoMedioExecucaoDto(quantidade, Math.Round(mediaMinutos, 2), Math.Round(mediaMinutos / 60, 2));
        }

        public async Task RemoverAsync(int id, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            // Uma OS que já entrou na Saga tem orçamento/pagamento em outros serviços: removê-la
            // deixaria esses dados órfãos. Nesses casos o caminho é cancelar.
            if (ordem.Status is not (StatusOrdemServico.Recebida or StatusOrdemServico.EmDiagnostico))
                throw new RegraDeNegocioException(
                    $"Só é possível remover OS 'Recebida' ou em 'Diagnóstico'. Status atual: {ordem.Status.Descricao()}. Use o cancelamento.");

            // Com a Saga ligada, a OS em diagnóstico já está com a Execução.
            if (_saga.Habilitada && ordem.Status != StatusOrdemServico.Recebida)
                throw new RegraDeNegocioException("Com a Saga ligada, só é possível remover OS 'Recebida'. Use o cancelamento.");

            _repositorio.Remover(ordem);
            await SalvarAsync(ct);
        }

        // ── Transições manuais ─────────────────────────────────────────────────

        public async Task<OrdemServicoDto> IniciarDiagnosticoAsync(int id, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            await IrParaDiagnosticoAsync(ordem, null, ct);
            await SalvarAsync(ct);

            return OrdemServicoDto.De(ordem);
        }

        public Task<OrdemServicoDto> AguardarAprovacaoAsync(int id, CancellationToken ct = default)
        {
            GarantirModoManual("aguardar-aprovacao");
            return TransicionarAsync(id, StatusOrdemServico.AguardandoAprovacao, null, ct);
        }

        public Task<OrdemServicoDto> AprovarAsync(int id, CancellationToken ct = default)
        {
            GarantirModoManual("aprovar");
            return TransicionarAsync(id, StatusOrdemServico.EmExecucao, "Aprovação manual", ct);
        }

        public Task<OrdemServicoDto> RecusarAsync(int id, string? motivo, CancellationToken ct = default)
        {
            GarantirModoManual("recusar");
            return TransicionarAsync(id, StatusOrdemServico.Cancelada,
                                     string.IsNullOrWhiteSpace(motivo) ? "Orçamento recusado" : $"Orçamento recusado: {motivo}", ct);
        }

        public Task<OrdemServicoDto> FinalizarAsync(int id, CancellationToken ct = default)
        {
            GarantirModoManual("finalizar");
            return TransicionarAsync(id, StatusOrdemServico.Finalizada, null, ct);
        }

        public Task<OrdemServicoDto> EntregarAsync(int id, CancellationToken ct = default) =>
            TransicionarAsync(id, StatusOrdemServico.Entregue, null, ct);

        public async Task<OrdemServicoDto> CancelarAsync(int id, string? motivo, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            // Antes do orçamento não há nada no Billing para desfazer; depois disso, o
            // cancelamento precisa passar pela compensação da Saga.
            if (ordem.Status is not (StatusOrdemServico.Recebida or StatusOrdemServico.EmDiagnostico)
                && _saga.Habilitada)
                throw new OperacaoControladaPelaSagaException("cancelar");

            var emDiagnostico = ordem.Status == StatusOrdemServico.EmDiagnostico;
            var texto = string.IsNullOrWhiteSpace(motivo) ? "Cancelada manualmente" : motivo;

            await MudarStatusAsync(ordem, StatusOrdemServico.Cancelada, texto, ct);

            // A Execução pode já ter reservado peças no diagnóstico: a Saga avisa e termina.
            if (_saga.Habilitada && emDiagnostico)
                await _publicador.PublicarAsync(new OrdemServicoCanceladaManualmente
                {
                    CorrelationId = ordem.CorrelationId,
                    Motivo        = texto,
                }, _contexto.CorrelationId, ct);

            await SalvarAsync(ct);

            return OrdemServicoDto.De(ordem);
        }

        // ── Itens ──────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ItemOrdemServicoDto>> ListarItensAsync(int ordemServicoId, CancellationToken ct = default) =>
            (await ObterOrdemAsync(ordemServicoId, ct)).Itens.Select(ItemOrdemServicoDto.De).ToList();

        public async Task<ItemOrdemServicoDto> ObterItemAsync(int ordemServicoId, int itemId, CancellationToken ct = default) =>
            ItemOrdemServicoDto.De((await ObterOrdemAsync(ordemServicoId, ct)).ObterItem(itemId));

        public async Task<ItemOrdemServicoDto> AdicionarItemAsync(int ordemServicoId, ItemOrdemServicoInputDto dto, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(ordemServicoId, ct);
            var item  = ordem.AdicionarItem(dto.Descricao, dto.Tipo, dto.Quantidade, dto.ValorUnitario,
                                            dto.EstoqueId, dto.ServicoOficinaId, _relogio.UtcAgora);

            await SalvarAsync(ct);
            return ItemOrdemServicoDto.De(item);
        }

        public async Task<ItemOrdemServicoDto> AtualizarItemAsync(int ordemServicoId, int itemId, ItemOrdemServicoInputDto dto, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(ordemServicoId, ct);
            var item  = ordem.AtualizarItem(itemId, dto.Descricao, dto.Tipo, dto.Quantidade, dto.ValorUnitario,
                                            dto.EstoqueId, dto.ServicoOficinaId, _relogio.UtcAgora);

            await SalvarAsync(ct);
            return ItemOrdemServicoDto.De(item);
        }

        public async Task RemoverItemAsync(int ordemServicoId, int itemId, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(ordemServicoId, ct);
            ordem.RemoverItem(itemId, _relogio.UtcAgora);

            await SalvarAsync(ct);
        }

        // ── Apoio ──────────────────────────────────────────────────────────────

        private async Task SalvarAsync(CancellationToken ct)
        {
            await _repositorio.SalvarAsync(ct);
            await _publicador.ConfirmarAsync(ct);
        }

        private async Task<OrdemServico> ObterOrdemAsync(int id, CancellationToken ct) =>
            await _repositorio.ObterPorIdAsync(id, ct)
            ?? throw new RecursoNaoEncontradoException($"Ordem de Serviço com Id '{id}' não encontrada.");

        private async Task<OrdemServicoDto> TransicionarAsync(int id, StatusOrdemServico novo, string? motivo, CancellationToken ct)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            await MudarStatusAsync(ordem, novo, motivo, ct);
            await SalvarAsync(ct);

            return OrdemServicoDto.De(ordem);
        }

        private async Task MudarStatusAsync(OrdemServico ordem, StatusOrdemServico novo, string? motivo, CancellationToken ct)
        {
            var (anterior, tempo) = ordem.AlterarStatus(novo, _relogio.UtcAgora, OrigemAlteracaoStatus.Usuario,
                                                        motivo, _contexto.CorrelationId);

            _monitoramento.RegistrarMudancaStatus(ordem, anterior, tempo);

            // Gravado no outbox: só sai para o broker se a transação for confirmada.
            if (_saga.Habilitada)
                await _publicador.PublicarAsync(new OrdemServicoStatusAlterado
                {
                    CorrelationId  = ordem.CorrelationId,
                    OrdemServicoId = ordem.Id,
                    DataHora       = _relogio.UtcAgora,
                    ClienteId      = ordem.ClienteId,
                    StatusAnterior = anterior.ToString(),
                    StatusAtual    = novo.ToString(),
                    Motivo         = motivo,
                }, _contexto.CorrelationId, ct);
        }

        /// <summary>
        /// Recebida → Em Diagnóstico. Com a Saga ligada, é aqui que ela começa: os dados do
        /// cliente que o Billing vai precisar são lidos agora, enquanto há o token da requisição.
        /// </summary>
        private async Task IrParaDiagnosticoAsync(OrdemServico ordem, string? motivo, CancellationToken ct)
        {
            if (!_saga.Habilitada)
            {
                await MudarStatusAsync(ordem, StatusOrdemServico.EmDiagnostico, motivo, ct);
                return;
            }

            var cliente = await _clienteVeiculo.ObterClienteAsync(ordem.ClienteId, ct)
                ?? throw new RecursoNaoEncontradoException($"Cliente com Id '{ordem.ClienteId}' não encontrado.");

            await MudarStatusAsync(ordem, StatusOrdemServico.EmDiagnostico, motivo, ct);

            await _publicador.PublicarAsync(new DiagnosticoSolicitado
            {
                CorrelationId           = ordem.CorrelationId,
                OrdemServicoId          = ordem.Id,
                ClienteId               = ordem.ClienteId,
                VeiculoId               = ordem.VeiculoId,
                DescricaoProblema       = ordem.DescricaoProblema,
                NomeCliente             = cliente.Nome,
                EmailCliente            = cliente.Email,
                CorrelationIdRequisicao = _contexto.CorrelationId,
            }, _contexto.CorrelationId, ct);
        }

        private void GarantirModoManual(string operacao)
        {
            if (_saga.Habilitada)
                throw new OperacaoControladaPelaSagaException(operacao);
        }
    }
}
