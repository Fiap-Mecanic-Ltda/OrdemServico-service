using OsService.Domain.Enums;
using OsService.Domain.Regras;

namespace OsService.UnitTests.Domain
{
    public class TransicoesDeStatusTests
    {
        [Theory]
        [InlineData(StatusOrdemServico.Recebida, StatusOrdemServico.EmDiagnostico)]
        [InlineData(StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoOrcamento)]
        [InlineData(StatusOrdemServico.AguardandoOrcamento, StatusOrdemServico.AguardandoAprovacao)]
        [InlineData(StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.AguardandoPagamento)]
        [InlineData(StatusOrdemServico.AguardandoPagamento, StatusOrdemServico.EmExecucao)]
        [InlineData(StatusOrdemServico.EmExecucao, StatusOrdemServico.Finalizada)]
        [InlineData(StatusOrdemServico.Finalizada, StatusOrdemServico.Entregue)]
        [InlineData(StatusOrdemServico.EmExecucao, StatusOrdemServico.Compensando)]
        [InlineData(StatusOrdemServico.Compensando, StatusOrdemServico.Cancelada)]
        [InlineData(StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao)]
        public void Caminho_da_saga_e_do_fluxo_manual_e_permitido(StatusOrdemServico de, StatusOrdemServico para)
        {
            Assert.True(TransicoesDeStatus.Permitida(de, para));
        }

        [Theory]
        [InlineData(StatusOrdemServico.Recebida, StatusOrdemServico.Finalizada)]
        [InlineData(StatusOrdemServico.Entregue, StatusOrdemServico.Recebida)]
        [InlineData(StatusOrdemServico.Cancelada, StatusOrdemServico.Recebida)]
        [InlineData(StatusOrdemServico.Finalizada, StatusOrdemServico.Cancelada)]
        [InlineData(StatusOrdemServico.EmExecucao, StatusOrdemServico.Cancelada)]
        public void Saltos_e_saidas_de_status_final_nao_sao_permitidos(StatusOrdemServico de, StatusOrdemServico para)
        {
            Assert.False(TransicoesDeStatus.Permitida(de, para));
        }

        [Fact]
        public void Status_finais_nao_tem_destino()
        {
            Assert.Empty(TransicoesDeStatus.DestinosDe(StatusOrdemServico.Entregue));
            Assert.Empty(TransicoesDeStatus.DestinosDe(StatusOrdemServico.Cancelada));
            Assert.Empty(TransicoesDeStatus.DestinosDe((StatusOrdemServico)99));
        }

        [Fact]
        public void Todo_status_tem_regra_definida()
        {
            foreach (var status in Enum.GetValues<StatusOrdemServico>())
                Assert.NotNull(TransicoesDeStatus.DestinosDe(status));
        }
    }
}
