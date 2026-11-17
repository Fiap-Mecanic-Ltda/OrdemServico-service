using Microsoft.Extensions.Configuration;

namespace OsService.Infrastructure.Simuladores
{
    /// <summary>
    /// Quais simuladores sobem junto com a Saga. <c>Simuladores:Habilitados</c> liga os dois;
    /// <c>Simuladores:Execucao</c> e <c>Simuladores:Billing</c>, quando informados, valem só para
    /// aquele serviço. Assim a Execução real pode responder aos comandos enquanto o Billing
    /// continua simulado.
    /// </summary>
    public sealed record SimuladoresAtivos(bool Execucao, bool Billing)
    {
        public static SimuladoresAtivos Ler(IConfiguration configuration)
        {
            var todos = configuration.GetValue<bool>("Simuladores:Habilitados");
            return new SimuladoresAtivos(
                configuration.GetValue<bool?>("Simuladores:Execucao") ?? todos,
                configuration.GetValue<bool?>("Simuladores:Billing") ?? todos);
        }
    }
}
