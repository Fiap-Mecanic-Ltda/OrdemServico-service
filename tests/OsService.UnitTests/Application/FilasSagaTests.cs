using MechanicLtda.Contratos;
using OsService.Application.Saga;

namespace OsService.UnitTests.Application
{
    public class FilasSagaTests
    {
        [Theory]
        [InlineData(typeof(OrcamentoAprovado), "os-orcamento-aprovado")]
        [InlineData(typeof(DiagnosticoConcluido), "os-diagnostico-concluido")]
        [InlineData(typeof(PagamentoEstornado), "os-pagamento-estornado")]
        public void Fila_de_cada_evento_segue_o_contrato(Type evento, string fila)
        {
            Assert.Equal(fila, FilasSaga.FilaDoEvento(evento));
        }

        [Fact]
        public void Mensagens_internas_vao_direto_para_a_saga()
        {
            Assert.True(FilasSaga.EhInterna(typeof(DiagnosticoSolicitado)));
            Assert.True(FilasSaga.EhInterna(typeof(PrazoAprovacaoExpirado)));
            Assert.False(FilasSaga.EhInterna(typeof(GerarOrcamento)));
        }

        [Fact]
        public void Todos_os_eventos_externos_tem_fila_propria()
        {
            Assert.Equal(13, FilasSaga.EventosExternos.Select(FilasSaga.FilaDoEvento).Distinct().Count());
        }
    }
}
