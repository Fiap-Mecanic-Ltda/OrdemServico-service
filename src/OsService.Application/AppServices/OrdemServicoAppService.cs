using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsService.Application.DTOs;
using OsService.Application.Exceptions;
using OsService.Application.Interfaces;
using OsService.Application.Options;
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
            [StatusOrdemServico.AguardandoPagamento] = 2,
            [StatusOrdemServico.AguardandoAprovacao] = 3,
            [StatusOrdemServico.AguardandoOrcamento] = 4,
            [StatusOrdemServico.EmDiagnostico]       = 5,
            [StatusOrdemServico.Compensando]         = 6,
            [StatusOrdemServico.Recebida]            = 7,
        };

        private readonly IOrdemServicoRepository _repositorio;
        private readonly IClienteVeiculoGateway _clienteVeiculo;
        private readonly IMonitoramentoService _monitoramento;
        private readonly IContextoRequisicao _contexto;
        private readonly IRelogio _relogio;
        private readonly SagaOptions _saga;
        private readonly ILogger<OrdemServicoAppService> _logger;

        public OrdemServicoAppService(IOrdemServicoRepository repositorio, IClienteVeiculoGateway clienteVeiculo,
                                      IMonitoramentoService monitoramento, IContextoRequisicao contexto,
                                      IRelogio relogio, IOptions<SagaOptions> saga,
                                      ILogger<OrdemServicoAppService> logger)
        {
            _repositorio    = repositorio;
            _clienteVeiculo = clienteVeiculo;
            _monitoramento  = monitoramento;
            _contexto       = contexto;
            _relogio        = relogio;
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
            await _repositorio.SalvarAsync(ct);

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
                Transicionar(ordem, StatusOrdemServico.EmDiagnostico, "Valor estimado informado");

            await _repositorio.SalvarAsync(ct);
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

            _repositorio.Remover(ordem);
            await _repositorio.SalvarAsync(ct);
        }

        // ── Transições manuais ─────────────────────────────────────────────────

        public Task<OrdemServicoDto> IniciarDiagnosticoAsync(int id, CancellationToken ct = default) =>
            TransicionarAsync(id, StatusOrdemServico.EmDiagnostico, null, ct);

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

            // Antes do orçamento não há nada em outro serviço para desfazer; depois disso, o
            // cancelamento precisa passar pela compensação da Saga.
            if (ordem.Status is not (StatusOrdemServico.Recebida or StatusOrdemServico.EmDiagnostico)
                && _saga.Habilitada)
                throw new OperacaoControladaPelaSagaException("cancelar");

            Transicionar(ordem, StatusOrdemServico.Cancelada,
                         string.IsNullOrWhiteSpace(motivo) ? "Cancelada manualmente" : motivo);
            await _repositorio.SalvarAsync(ct);

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

            await _repositorio.SalvarAsync(ct);
            return ItemOrdemServicoDto.De(item);
        }

        public async Task<ItemOrdemServicoDto> AtualizarItemAsync(int ordemServicoId, int itemId, ItemOrdemServicoInputDto dto, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(ordemServicoId, ct);
            var item  = ordem.AtualizarItem(itemId, dto.Descricao, dto.Tipo, dto.Quantidade, dto.ValorUnitario,
                                            dto.EstoqueId, dto.ServicoOficinaId, _relogio.UtcAgora);

            await _repositorio.SalvarAsync(ct);
            return ItemOrdemServicoDto.De(item);
        }

        public async Task RemoverItemAsync(int ordemServicoId, int itemId, CancellationToken ct = default)
        {
            var ordem = await ObterOrdemAsync(ordemServicoId, ct);
            ordem.RemoverItem(itemId, _relogio.UtcAgora);

            await _repositorio.SalvarAsync(ct);
        }

        // ── Apoio ──────────────────────────────────────────────────────────────

        private async Task<OrdemServico> ObterOrdemAsync(int id, CancellationToken ct) =>
            await _repositorio.ObterPorIdAsync(id, ct)
            ?? throw new RecursoNaoEncontradoException($"Ordem de Serviço com Id '{id}' não encontrada.");

        private async Task<OrdemServicoDto> TransicionarAsync(int id, StatusOrdemServico novo, string? motivo, CancellationToken ct)
        {
            var ordem = await ObterOrdemAsync(id, ct);

            Transicionar(ordem, novo, motivo);
            await _repositorio.SalvarAsync(ct);

            return OrdemServicoDto.De(ordem);
        }

        private void Transicionar(OrdemServico ordem, StatusOrdemServico novo, string? motivo)
        {
            var (anterior, tempo) = ordem.AlterarStatus(novo, _relogio.UtcAgora, OrigemAlteracaoStatus.Usuario,
                                                        motivo, _contexto.CorrelationId);

            _monitoramento.RegistrarMudancaStatus(ordem, anterior, tempo);
        }

        private void GarantirModoManual(string operacao)
        {
            if (_saga.Habilitada)
                throw new OperacaoControladaPelaSagaException(operacao);
        }
    }
}
