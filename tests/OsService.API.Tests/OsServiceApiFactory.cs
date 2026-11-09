using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using OsService.Application.Interfaces;
using OsService.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace OsService.API.Tests
{
    /// <summary>
    /// Sobe a API em memória: chave JWT de teste, banco EF em memória no lugar do SQL Server e
    /// um gateway falso no lugar da API do MechanicLtda.
    /// </summary>
    public class OsServiceApiFactory : WebApplicationFactory<Program>
    {
        public const string ChaveJwtTeste = "chave-jwt-de-teste-com-pelo-menos-32-caracteres";

        private readonly string _banco = Guid.NewGuid().ToString();

        public GatewayFalso Gateway { get; } = new();

        protected virtual bool SagaHabilitada => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("JWT_SECRET_KEY", ChaveJwtTeste);
            builder.UseSetting("Saga:Habilitada", SagaHabilitada.ToString());
            // Com a Saga ligada: broker em memória e Billing/Execução simulados no próprio processo.
            builder.UseSetting("Mensageria:Transporte", "InMemory");
            builder.UseSetting("Simuladores:Habilitados", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<OsServiceContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<OsServiceContext>>();
                services.AddDbContext<OsServiceContext>(o => o.UseInMemoryDatabase(_banco));

                services.RemoveAll<IClienteVeiculoGateway>();
                services.AddSingleton<IClienteVeiculoGateway>(Gateway);
            });
        }

        public HttpClient ClienteComRole(string role, int? clienteId = null)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GerarToken(role, clienteId));
            return client;
        }

        public static string GerarToken(string role, int? clienteId = null)
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "teste"), new(ClaimTypes.Role, role) };
            if (clienteId.HasValue)
                claims.Add(new Claim("clienteId", clienteId.Value.ToString()));

            var token = new JwtSecurityToken(
                issuer: clienteId.HasValue ? "MechanicLtda.Auth.Cpf" : "MechanicLtda.API",
                audience: "MechanicLtda.Clients",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveJwtTeste)), SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    /// <summary>Mesma API com a Saga ligada, rodando ponta a ponta com os simuladores.</summary>
    public class OsServiceApiFactoryComSaga : OsServiceApiFactory
    {
        protected override bool SagaHabilitada => true;
    }

    /// <summary>Gateway do MechanicLtda em memória: veículos cadastrados ou "fora do ar".</summary>
    public class GatewayFalso : IClienteVeiculoGateway
    {
        private readonly Dictionary<int, VeiculoResumo> _veiculos = new()
        {
            [10] = new VeiculoResumo(10, 1, "ABC1D23"),
            [20] = new VeiculoResumo(20, 2, "XYZ9K87"),
        };

        public bool ForaDoAr { get; set; }

        public Task<ClienteResumo?> ObterClienteAsync(int clienteId, CancellationToken ct = default)
        {
            GarantirNoAr();
            return Task.FromResult<ClienteResumo?>(clienteId is 1 or 2 ? new ClienteResumo(clienteId, "Cliente", "c@x.com") : null);
        }

        public Task<VeiculoResumo?> ObterVeiculoAsync(int veiculoId, CancellationToken ct = default)
        {
            GarantirNoAr();
            return Task.FromResult(_veiculos.GetValueOrDefault(veiculoId));
        }

        private void GarantirNoAr()
        {
            if (ForaDoAr)
                throw new OsService.Application.Exceptions.IntegracaoIndisponivelException("MechanicLtda fora do ar.");
        }
    }
}
