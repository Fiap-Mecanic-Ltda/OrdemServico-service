using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Application.Saga;
using OsService.Infrastructure;
using OsService.Infrastructure.Mensageria;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace OsService.UnitTests.Infrastructure
{
    public class VerificadorPrazosSagaTests
    {
        private static readonly DateTime Agora = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<ISendEndpoint> _destino = new();
        private readonly List<object> _enviadas = [];
        private readonly ServiceProvider _provider;

        public VerificadorPrazosSagaTests()
        {
            _destino.Setup(d => d.Send(It.IsAny<object>(), It.IsAny<Type>(), It.IsAny<IPipe<SendContext>>(), It.IsAny<CancellationToken>()))
                    .Callback<object, Type, IPipe<SendContext>, CancellationToken>((m, _, _, _) => _enviadas.Add(m))
                    .Returns(Task.CompletedTask);

            var send = new Mock<ISendEndpointProvider>();
            send.Setup(s => s.GetSendEndpoint(FilasSaga.EnderecoSaga)).ReturnsAsync(_destino.Object);

            var relogio = new Mock<IRelogio>();
            relogio.Setup(r => r.UtcAgora).Returns(Agora);

            var banco = Guid.NewGuid().ToString();
            _provider = new ServiceCollection()
                .AddDbContext<OsServiceContext>(o => o.UseInMemoryDatabase(banco))
                .AddSingleton(send.Object)
                .AddSingleton(relogio.Object)
                .BuildServiceProvider();
        }

        private async Task SemearAsync(params OrdemServicoSagaState[] sagas)
        {
            using var scope = _provider.CreateScope();
            var contexto = scope.ServiceProvider.GetRequiredService<OsServiceContext>();
            contexto.AddRange(sagas);
            await contexto.SaveChangesAsync();
        }

        private VerificadorPrazosSaga Criar() =>
            new(_provider.GetRequiredService<IServiceScopeFactory>(), MsOptions.Create(new SagaOptions()),
                NullLogger<VerificadorPrazosSaga>.Instance);

        [Fact]
        public async Task Envia_o_evento_certo_so_para_os_prazos_vencidos()
        {
            var diagnosticoVencido = Guid.NewGuid();
            var aprovacaoVencida   = Guid.NewGuid();
            await SemearAsync(
                new OrdemServicoSagaState { CorrelationId = diagnosticoVencido, EstadoAtual = "Diagnosticando", PrazoDiagnostico = Agora.AddMinutes(-1) },
                new OrdemServicoSagaState { CorrelationId = aprovacaoVencida, EstadoAtual = "AguardandoAprovacao", PrazoAprovacao = Agora.AddHours(-1) },
                new OrdemServicoSagaState { CorrelationId = Guid.NewGuid(), EstadoAtual = "Diagnosticando", PrazoDiagnostico = Agora.AddHours(1) },
                new OrdemServicoSagaState { CorrelationId = Guid.NewGuid(), EstadoAtual = "Executando", PrazoAprovacao = Agora.AddHours(-1) });

            var quantidade = await Criar().VerificarAsync(CancellationToken.None);

            Assert.Equal(2, quantidade);
            Assert.Contains(_enviadas, m => m is PrazoDiagnosticoExpirado p && p.CorrelationId == diagnosticoVencido);
            Assert.Contains(_enviadas, m => m is PrazoAprovacaoExpirado p && p.CorrelationId == aprovacaoVencida);
        }

        [Fact]
        public async Task Sem_prazos_vencidos_nao_envia_nada()
        {
            Assert.Equal(0, await Criar().VerificarAsync(CancellationToken.None));
            Assert.Empty(_enviadas);
        }
    }
}
