using Reqnroll;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OsService.API.Tests.Bdd
{
    /// <summary>
    /// Passos dos cenários BDD. A API sobe em memória com a Saga ligada e os simuladores de
    /// Billing e Execução (o mesmo ambiente dos testes de API da Saga).
    /// </summary>
    [Binding]
    public class PassosOrdemServico
    {
        private static readonly Lazy<OsServiceApiFactoryComSaga> Fabrica = new(() => new OsServiceApiFactoryComSaga());

        private HttpClient _cliente = null!;
        private int _ordemId;
        private List<JsonElement> _historico = [];

        [AfterTestRun]
        public static void Encerrar()
        {
            if (Fabrica.IsValueCreated)
                Fabrica.Value.Dispose();
        }

        [Given("que a Saga está ligada com Billing e Execução simulados")]
        public void DadoSagaLigada() => _ = Fabrica.Value;

        [Given("que estou autenticado como funcionário")]
        public void DadoAutenticado() => _cliente = Fabrica.Value.ClienteComRole("Funcionario");

        [Given("uma OS aberta para o cliente {int} e o veículo {int} com o problema {string}")]
        public async Task DadoOsAberta(int clienteId, int veiculoId, string problema)
        {
            var resposta = await _cliente.PostAsJsonAsync("/api/ordemservico",
                new { clienteId, veiculoId, descricaoProblema = problema });
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

            _ordemId = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        [When("eu inicio o diagnóstico")]
        public async Task QuandoInicioDiagnostico()
        {
            var resposta = await _cliente.PatchAsync($"/api/ordemservico/{_ordemId}/iniciar-diagnostico", null);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        [Given("que eu iniciei o diagnóstico e a OS foi finalizada")]
        public async Task DadoOsFinalizada()
        {
            await QuandoInicioDiagnostico();
            await EntaoChegaAoStatus("Finalizada");
        }

        [When("eu registro a entrega")]
        public async Task QuandoRegistroEntrega()
        {
            var resposta = await _cliente.PatchAsync($"/api/ordemservico/{_ordemId}/entregar", null);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }

        /// <summary>A Saga é assíncrona: consulta o histórico até o status aparecer.</summary>
        [Then("a OS chega ao status {string}")]
        public async Task EntaoChegaAoStatus(string status)
        {
            var limite = DateTime.UtcNow.AddSeconds(20);
            do
            {
                _historico = (await _cliente.GetFromJsonAsync<List<JsonElement>>($"/api/ordemservico/{_ordemId}/historico"))!;
                if (_historico.Any(h => h.GetProperty("statusNovoDescricao").GetString() == status))
                    return;
                await Task.Delay(100);
            } while (DateTime.UtcNow < limite);

            Assert.Fail($"A OS {_ordemId} não chegou a '{status}'. Histórico: {string.Join(", ", Descricoes())}");
        }

        [Then("a OS fica com o status {string}")]
        public async Task EntaoFicaComStatus(string status)
        {
            var os = await _cliente.GetFromJsonAsync<JsonElement>($"/api/ordemservico/{_ordemId}");
            Assert.Equal(status, os.GetProperty("statusDescricao").GetString());
        }

        [Then("o histórico mostra os status {string}")]
        public void EntaoHistoricoMostra(string lista) =>
            Assert.Equal(lista.Split(',', StringSplitOptions.TrimEntries), Descricoes());

        [Then("as mudanças depois do diagnóstico foram feitas pela Saga")]
        public void EntaoFeitasPelaSaga() =>
            Assert.All(_historico.Skip(2), h => Assert.Equal("Saga", h.GetProperty("origem").GetString()));

        [Then("a OS guarda o orçamento e o pagamento")]
        public async Task EntaoGuardaOrcamentoEPagamento()
        {
            var os = await _cliente.GetFromJsonAsync<JsonElement>($"/api/ordemservico/{_ordemId}");
            Assert.Equal(JsonValueKind.Number, os.GetProperty("orcamentoId").ValueKind);
            Assert.Equal(JsonValueKind.Number, os.GetProperty("pagamentoId").ValueKind);
        }

        [Then("a OS não tem pagamento registrado")]
        public async Task EntaoSemPagamento()
        {
            var os = await _cliente.GetFromJsonAsync<JsonElement>($"/api/ordemservico/{_ordemId}");
            Assert.Equal(JsonValueKind.Null, os.GetProperty("pagamentoId").ValueKind);
        }

        [Then("o motivo da compensação começa com {string}")]
        public void EntaoMotivoCompensacao(string inicio)
        {
            var compensando = _historico.Single(h => h.GetProperty("statusNovoDescricao").GetString() == "Compensando");
            Assert.StartsWith(inicio, compensando.GetProperty("motivo").GetString());
        }

        private List<string?> Descricoes() =>
            _historico.Select(h => h.GetProperty("statusNovoDescricao").GetString()).ToList();
    }
}
