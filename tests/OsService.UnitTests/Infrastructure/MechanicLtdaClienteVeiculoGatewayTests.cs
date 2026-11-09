using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OsService.Application.Exceptions;
using OsService.Application.Interfaces;
using OsService.Infrastructure.Gateways;
using System.Net;
using System.Text;

namespace OsService.UnitTests.Infrastructure
{
    public class MechanicLtdaClienteVeiculoGatewayTests
    {
        private sealed class HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
        {
            public HttpRequestMessage? Ultima { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Ultima = request;
                return Task.FromResult(responder(request));
            }
        }

        private readonly Mock<IMonitoramentoService> _monitoramento = new();
        private readonly Mock<IContextoRequisicao> _contexto = new();

        private (MechanicLtdaClienteVeiculoGateway Gateway, HandlerFalso Handler) Criar(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var handler = new HandlerFalso(responder);
            var http    = new HttpClient(handler) { BaseAddress = new Uri("http://mechanicltda/") };

            return (new MechanicLtdaClienteVeiculoGateway(http, _contexto.Object, _monitoramento.Object,
                NullLogger<MechanicLtdaClienteVeiculoGateway>.Instance), handler);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task Le_o_veiculo_e_repassa_token_e_correlation_id()
        {
            _contexto.Setup(c => c.Authorization).Returns("Bearer abc");
            _contexto.Setup(c => c.CorrelationId).Returns("req-9");
            var (gateway, handler) = Criar(_ => Json("""{"id":2,"clienteId":1,"placa":"ABC1D23","modelo":"Gol"}"""));

            var veiculo = await gateway.ObterVeiculoAsync(2);

            Assert.Equal(new VeiculoResumo(2, 1, "ABC1D23"), veiculo);
            Assert.Equal("http://mechanicltda/api/veiculo/2", handler.Ultima!.RequestUri!.ToString());
            Assert.Equal("Bearer abc", handler.Ultima.Headers.Authorization!.ToString());
            Assert.Equal("req-9", Assert.Single(handler.Ultima.Headers.GetValues("X-Correlation-Id")));
        }

        [Fact]
        public async Task Le_o_cliente()
        {
            var (gateway, handler) = Criar(_ => Json("""{"id":1,"nome":"Ana","email":"ana@x.com"}"""));

            var cliente = await gateway.ObterClienteAsync(1);

            Assert.Equal(new ClienteResumo(1, "Ana", "ana@x.com"), cliente);
            Assert.False(handler.Ultima!.Headers.Contains("Authorization"));
        }

        [Fact]
        public async Task Recurso_inexistente_devolve_null()
        {
            var (gateway, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            Assert.Null(await gateway.ObterVeiculoAsync(2));
            _monitoramento.Verify(m => m.RegistrarFalhaIntegracao(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public async Task Erro_do_MechanicLtda_vira_integracao_indisponivel(HttpStatusCode status)
        {
            var (gateway, _) = Criar(_ => new HttpResponseMessage(status));

            await Assert.ThrowsAsync<IntegracaoIndisponivelException>(() => gateway.ObterClienteAsync(1));
            _monitoramento.Verify(m => m.RegistrarFalhaIntegracao("MechanicLtda", "ObterCliente", It.IsAny<IntegracaoIndisponivelException>()), Times.Once);
        }

        [Fact]
        public async Task Falha_de_rede_vira_integracao_indisponivel()
        {
            var (gateway, _) = Criar(_ => throw new HttpRequestException("connection refused"));

            var ex = await Assert.ThrowsAsync<IntegracaoIndisponivelException>(() => gateway.ObterVeiculoAsync(2));
            Assert.IsType<HttpRequestException>(ex.InnerException);
            _monitoramento.Verify(m => m.RegistrarFalhaIntegracao("MechanicLtda", "ObterVeiculo", It.IsAny<Exception>()), Times.Once);
        }
    }
}
