using Microsoft.Extensions.Logging.Abstractions;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Infrastructure.Monitoramento;

namespace OsService.UnitTests.Infrastructure
{
    /// <summary>Sem o agente do New Relic carregado, os registros não podem lançar exceção.</summary>
    public class NewRelicMonitoramentoServiceTests
    {
        private readonly NewRelicMonitoramentoService _servico = new(NullLogger<NewRelicMonitoramentoService>.Instance);

        [Fact]
        public void Registros_funcionam_sem_o_agente()
        {
            var os = OrdemServico.Abrir(1, 1, "Teste", null, DateTime.UtcNow, null);

            var ex = Record.Exception(() =>
            {
                _servico.RegistrarOrdemServicoCriada(os);
                _servico.RegistrarMudancaStatus(os, StatusOrdemServico.Recebida, TimeSpan.FromMinutes(3));
                _servico.RegistrarFalhaIntegracao("MechanicLtda", "ObterCliente", new Exception(new string('x', 400)));
            });

            Assert.Null(ex);
        }
    }
}
