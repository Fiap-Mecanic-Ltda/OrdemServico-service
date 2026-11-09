using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsService.Infrastructure;

namespace OsService.Bootstrap.Extensions
{
    public static class MigrationExtension
    {
        /// <summary>
        /// Aplica as migrations na subida, com novas tentativas: no Kubernetes e no compose o
        /// banco pode ainda estar iniciando. O startupProbe do deployment cobre esse tempo.
        /// </summary>
        public static async Task MigrateDatabaseAsync(this WebApplication app, int tentativas = 10)
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OsServiceContext>();
            var logger  = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

            if (!context.Database.IsRelational())
                return;

            for (var tentativa = 1; ; tentativa++)
            {
                try
                {
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Migrations aplicadas.");
                    return;
                }
                catch (Exception ex) when (tentativa < tentativas)
                {
                    logger.LogWarning(ex, "Banco indisponível (tentativa {Tentativa} de {Total}); nova tentativa em 10s.", tentativa, tentativas);
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }
    }
}
