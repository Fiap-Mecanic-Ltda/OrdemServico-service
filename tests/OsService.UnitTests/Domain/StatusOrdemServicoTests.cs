using OsService.Domain.Enums;

namespace OsService.UnitTests.Domain
{
    public class StatusOrdemServicoTests
    {
        [Theory]
        [InlineData(StatusOrdemServico.Recebida, 1)]
        [InlineData(StatusOrdemServico.EmDiagnostico, 2)]
        [InlineData(StatusOrdemServico.AguardandoAprovacao, 3)]
        [InlineData(StatusOrdemServico.EmExecucao, 4)]
        [InlineData(StatusOrdemServico.Finalizada, 5)]
        [InlineData(StatusOrdemServico.Entregue, 6)]
        public void Status_da_fase_3_mantem_os_mesmos_valores(StatusOrdemServico status, int valor)
        {
            Assert.Equal(valor, (int)status);
        }

        [Theory]
        [InlineData(StatusOrdemServico.EmDiagnostico, "Diagnóstico")]
        [InlineData(StatusOrdemServico.AguardandoOrcamento, "Aguardando Orçamento")]
        [InlineData(StatusOrdemServico.Cancelada, "Cancelada")]
        public void Descricao_usa_o_nome_de_exibicao(StatusOrdemServico status, string esperado)
        {
            Assert.Equal(esperado, status.Descricao());
        }

        [Fact]
        public void Descricao_de_valor_fora_do_enum_devolve_o_numero()
        {
            Assert.Equal("99", ((StatusOrdemServico)99).Descricao());
        }

        [Theory]
        [InlineData(StatusOrdemServico.Entregue, true)]
        [InlineData(StatusOrdemServico.Cancelada, true)]
        [InlineData(StatusOrdemServico.Finalizada, false)]
        [InlineData(StatusOrdemServico.Compensando, false)]
        public void EhFinal_so_para_entregue_e_cancelada(StatusOrdemServico status, bool esperado)
        {
            Assert.Equal(esperado, status.EhFinal());
        }
    }
}
