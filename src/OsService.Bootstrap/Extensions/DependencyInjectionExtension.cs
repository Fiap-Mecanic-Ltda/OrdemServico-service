using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OsService.Application.AppServices;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Infrastructure.Gateways;
using OsService.Infrastructure.Monitoramento;
using OsService.Infrastructure.Repositories;

namespace OsService.Bootstrap.Extensions
{
    public static class DependencyInjectionExtension
    {
        public static IServiceCollection AddDependencyInjection(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SagaOptions>(configuration.GetSection(SagaOptions.Secao));

            services.AddSingleton<IRelogio, RelogioSistema>();
            services.AddSingleton<IMonitoramentoService, NewRelicMonitoramentoService>();

            services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();
            services.AddScoped<IOrdemServicoAppService, OrdemServicoAppService>();

            // Cliente e veículo vêm do MechanicLtda por REST. Dentro do cluster o endereço é o
            // Service do Kubernetes (ex.: http://mechanicltda-api.mechanicltda.svc.cluster.local).
            services.AddHttpClient<IClienteVeiculoGateway, MechanicLtdaClienteVeiculoGateway>(http =>
                {
                    var baseUrl = configuration["Integracoes:MechanicLtdaApi:BaseUrl"] ?? "http://localhost:8080";
                    http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                })
                // Retry com backoff, circuit breaker e timeouts padrão do .NET.
                .AddStandardResilienceHandler();

            return services;
        }
    }
}
