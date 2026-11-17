using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OsService.Infrastructure;

namespace OsService.Bootstrap.Extensions
{
    public static class DatabaseExtension
    {
        public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<OsServiceContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 5)));

            return services;
        }
    }
}
