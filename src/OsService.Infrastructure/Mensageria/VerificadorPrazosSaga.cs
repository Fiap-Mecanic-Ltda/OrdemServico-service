using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Application.Saga;

namespace OsService.Infrastructure.Mensageria
{
    /// <summary>
    /// Procura Sagas com prazo de diagnóstico ou de aprovação vencido e publica o evento de
    /// prazo expirado para a própria Saga compensar. Os prazos ficam no banco, então nada se
    /// perde se o pod reiniciar, e não é preciso plugin de mensagens atrasadas no RabbitMQ.
    /// Se o evento sair duas vezes, a segunda é ignorada pela Saga (o estado já mudou).
    /// </summary>
    public class VerificadorPrazosSaga : BackgroundService
    {
        private const int LoteMaximo = 100;

        private readonly IServiceScopeFactory _scopes;
        private readonly SagaOptions _opcoes;
        private readonly ILogger<VerificadorPrazosSaga> _logger;

        public VerificadorPrazosSaga(IServiceScopeFactory scopes, IOptions<SagaOptions> opcoes, ILogger<VerificadorPrazosSaga> logger)
        {
            _scopes = scopes;
            _opcoes = opcoes.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalo = TimeSpan.FromSeconds(Math.Max(5, _opcoes.IntervaloVerificacaoPrazosSegundos));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await VerificarAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Falha ao verificar os prazos da Saga.");
                }

                try
                {
                    await Task.Delay(intervalo, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public async Task<int> VerificarAsync(CancellationToken ct)
        {
            using var scope = _scopes.CreateScope();
            var contexto = scope.ServiceProvider.GetRequiredService<OsServiceContext>();
            var send     = scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>();
            var agora    = scope.ServiceProvider.GetRequiredService<IRelogio>().UtcAgora;

            var vencidas = await contexto.Set<OrdemServicoSagaState>()
                .AsNoTracking()
                .Where(s => (s.EstadoAtual == "Diagnosticando" && s.PrazoDiagnostico < agora)
                         || (s.EstadoAtual == "AguardandoAprovacao" && s.PrazoAprovacao < agora))
                .OrderBy(s => s.DataInicio)
                .Take(LoteMaximo)
                .Select(s => new { s.CorrelationId, s.EstadoAtual, s.CorrelationIdRequisicao })
                .ToListAsync(ct);

            var destino = vencidas.Count > 0 ? await send.GetSendEndpoint(FilasSaga.EnderecoSaga) : null;

            foreach (var saga in vencidas)
            {
                object evento = saga.EstadoAtual == "Diagnosticando"
                    ? new PrazoDiagnosticoExpirado { CorrelationId = saga.CorrelationId }
                    : new PrazoAprovacaoExpirado { CorrelationId = saga.CorrelationId };

                await destino!.Send(evento, evento.GetType(),
                    p => CabecalhosSaga.Aplicar(p, saga.CorrelationId, saga.CorrelationIdRequisicao), ct);
                _logger.LogInformation("Saga {CorrelationId}: prazo vencido no estado {Estado}.", saga.CorrelationId, saga.EstadoAtual);
            }

            // Bus outbox: os eventos são gravados no banco e enviados depois do commit.
            if (vencidas.Count > 0)
                await contexto.SaveChangesAsync(ct);

            return vencidas.Count;
        }
    }
}
