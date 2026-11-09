using Microsoft.Extensions.Logging;
using OsService.Application.Exceptions;
using OsService.Application.Interfaces;
using System.Net;
using System.Net.Http.Json;

namespace OsService.Infrastructure.Gateways
{
    /// <summary>
    /// Lê cliente e veículo na API do MechanicLtda (dono desses dados), repassando o JWT e o
    /// X-Correlation-Id da requisição. Timeout e retry vêm do handler de resiliência
    /// registrado no Bootstrap.
    /// </summary>
    public class MechanicLtdaClienteVeiculoGateway : IClienteVeiculoGateway
    {
        public const string NomeHttpClient = "MechanicLtdaApi";

        private readonly HttpClient _http;
        private readonly IContextoRequisicao _contexto;
        private readonly IMonitoramentoService _monitoramento;
        private readonly ILogger<MechanicLtdaClienteVeiculoGateway> _logger;

        public MechanicLtdaClienteVeiculoGateway(HttpClient http, IContextoRequisicao contexto,
                                                IMonitoramentoService monitoramento,
                                                ILogger<MechanicLtdaClienteVeiculoGateway> logger)
        {
            _http          = http;
            _contexto      = contexto;
            _monitoramento = monitoramento;
            _logger        = logger;
        }

        public async Task<ClienteResumo?> ObterClienteAsync(int clienteId, CancellationToken ct = default)
        {
            var cliente = await GetAsync<ClienteResposta>($"api/cliente/{clienteId}", "ObterCliente", ct);

            return cliente is null ? null : new ClienteResumo(cliente.Id, cliente.Nome ?? string.Empty, cliente.Email ?? string.Empty);
        }

        public async Task<VeiculoResumo?> ObterVeiculoAsync(int veiculoId, CancellationToken ct = default)
        {
            var veiculo = await GetAsync<VeiculoResposta>($"api/veiculo/{veiculoId}", "ObterVeiculo", ct);

            return veiculo is null ? null : new VeiculoResumo(veiculo.Id, veiculo.ClienteId, veiculo.Placa);
        }

        private async Task<T?> GetAsync<T>(string caminho, string operacao, CancellationToken ct) where T : class
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Get, caminho);

            if (!string.IsNullOrWhiteSpace(_contexto.Authorization))
                requisicao.Headers.TryAddWithoutValidation("Authorization", _contexto.Authorization);
            if (!string.IsNullOrWhiteSpace(_contexto.CorrelationId))
                requisicao.Headers.TryAddWithoutValidation("X-Correlation-Id", _contexto.CorrelationId);

            HttpResponseMessage resposta;
            try
            {
                resposta = await _http.SendAsync(requisicao, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw Falha(operacao, "O serviço de clientes e veículos (MechanicLtda) não respondeu.", ex);
            }

            using (resposta)
            {
                if (resposta.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (resposta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw Falha(operacao, "O MechanicLtda recusou o token desta requisição ao consultar clientes e veículos.", null);

                if (!resposta.IsSuccessStatusCode)
                    throw Falha(operacao, $"O MechanicLtda respondeu {(int)resposta.StatusCode} ao consultar clientes e veículos.", null);

                return await resposta.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            }
        }

        private IntegracaoIndisponivelException Falha(string operacao, string mensagem, Exception? causa)
        {
            var excecao = new IntegracaoIndisponivelException(mensagem, causa);
            _monitoramento.RegistrarFalhaIntegracao("MechanicLtda", operacao, excecao);
            _logger.LogWarning(causa, "Falha na integração com o MechanicLtda ({Operacao}): {Mensagem}", operacao, mensagem);
            return excecao;
        }

        private sealed record ClienteResposta(int Id, string? Nome, string? Email);

        private sealed record VeiculoResposta(int Id, int ClienteId, string? Placa);
    }
}
