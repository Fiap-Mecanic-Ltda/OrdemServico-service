using Microsoft.Extensions.Configuration;
using OsService.Infrastructure.Simuladores;

namespace OsService.UnitTests.Infrastructure
{
    public class SimuladoresAtivosTests
    {
        private static SimuladoresAtivos Ler(params (string Chave, string Valor)[] valores) =>
            SimuladoresAtivos.Ler(new ConfigurationBuilder()
                .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Chave, v.Valor)))
                .Build());

        [Fact]
        public void SemConfiguracao_DesligaOsDois()
        {
            Assert.Equal(new SimuladoresAtivos(false, false), Ler());
        }

        [Fact]
        public void Habilitados_LigaOsDois()
        {
            Assert.Equal(new SimuladoresAtivos(true, true), Ler(("Simuladores:Habilitados", "true")));
        }

        [Fact]
        public void ExecucaoReal_DesligaSoOSimuladorDaExecucao()
        {
            var ativos = Ler(("Simuladores:Habilitados", "true"), ("Simuladores:Execucao", "false"));

            Assert.False(ativos.Execucao);
            Assert.True(ativos.Billing);
        }

        [Fact]
        public void ChaveDoServico_LigaSoAquelePorCima()
        {
            var ativos = Ler(("Simuladores:Habilitados", "false"), ("Simuladores:Billing", "true"));

            Assert.False(ativos.Execucao);
            Assert.True(ativos.Billing);
        }
    }
}
