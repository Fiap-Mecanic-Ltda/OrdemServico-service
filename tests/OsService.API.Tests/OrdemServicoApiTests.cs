using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsService.API.Tests
{
    public class OrdemServicoApiTests : IClassFixture<OsServiceApiFactory>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly OsServiceApiFactory _factory;
        private readonly HttpClient _admin;

        public OrdemServicoApiTests(OsServiceApiFactory factory)
        {
            _factory = factory;
            _factory.Gateway.ForaDoAr = false;
            _admin = factory.ClienteComRole("Administrador");
        }

        private record Os(int Id, Guid CorrelationId, int Status, string StatusDescricao, int ClienteId,
                          decimal ValorTotalItens, List<Item> Itens);
        private record Item(int Id, string Descricao, decimal ValorTotal);
        private record Historico(int? StatusAnterior, int StatusNovo, string Origem, string? Motivo);
        private record Erro(bool Success, List<string> Errors);

        private async Task<Os> AbrirAsync(int clienteId = 1, int veiculoId = 10)
        {
            var resposta = await _admin.PostAsJsonAsync("/api/ordemservico",
                new { clienteId, veiculoId, descricaoProblema = "Barulho na suspensão" });
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            return (await resposta.Content.ReadFromJsonAsync<Os>(Json))!;
        }

        private async Task<Os> PatchAsync(int id, string acao, HttpStatusCode esperado = HttpStatusCode.OK)
        {
            var resposta = await _admin.PatchAsync($"/api/ordemservico/{id}/{acao}", null);
            Assert.Equal(esperado, resposta.StatusCode);
            return esperado == HttpStatusCode.OK ? (await resposta.Content.ReadFromJsonAsync<Os>(Json))! : null!;
        }

        [Fact]
        public async Task Sem_token_responde_401()
        {
            var resposta = await _factory.CreateClient().GetAsync("/api/ordemservico");

            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }

        [Fact]
        public async Task Abre_e_consulta_a_OS()
        {
            var aberta = await AbrirAsync();

            var lida = await _admin.GetFromJsonAsync<Os>($"/api/ordemservico/{aberta.Id}", Json);

            Assert.Equal(1, lida!.Status);
            Assert.Equal("Recebida", lida.StatusDescricao);
            Assert.NotEqual(Guid.Empty, lida.CorrelationId);
        }

        [Fact]
        public async Task Fluxo_manual_completo_grava_o_historico()
        {
            var os = await AbrirAsync();

            await PatchAsync(os.Id, "iniciar-diagnostico");
            await PatchAsync(os.Id, "aguardar-aprovacao");
            await PatchAsync(os.Id, "aprovar");
            await PatchAsync(os.Id, "finalizar");
            var entregue = await PatchAsync(os.Id, "entregar");

            Assert.Equal(6, entregue.Status);
            var historico = await _admin.GetFromJsonAsync<List<Historico>>($"/api/ordemservico/{os.Id}/historico", Json);
            Assert.NotNull(historico);
            Assert.Equal([1, 2, 3, 4, 5, 6], historico.Select(h => h.StatusNovo));
            Assert.Equal("Usuario", historico[0].Origem);

            var tempo = await _admin.GetFromJsonAsync<JsonElement>("/api/ordemservico/tempo-medio-execucao");
            Assert.True(tempo.GetProperty("quantidadeOrdensFinalizadas").GetInt32() >= 1);
        }

        [Fact]
        public async Task Recusa_cancela_a_OS()
        {
            var os = await AbrirAsync();
            await PatchAsync(os.Id, "iniciar-diagnostico");
            await PatchAsync(os.Id, "aguardar-aprovacao");

            var resposta = await _admin.PatchAsJsonAsync($"/api/ordemservico/{os.Id}/recusar", new { motivoRecusa = "Caro" });
            var recusada = await resposta.Content.ReadFromJsonAsync<Os>(Json);

            Assert.Equal(10, recusada!.Status);
            var listagem = await _admin.GetFromJsonAsync<List<Os>>("/api/ordemservico", Json);
            Assert.DoesNotContain(listagem!, o => o.Id == os.Id);
        }

        [Fact]
        public async Task Transicao_invalida_responde_400_no_envelope_de_erro()
        {
            var os = await AbrirAsync();

            var resposta = await _admin.PatchAsync($"/api/ordemservico/{os.Id}/finalizar", null);
            var erro = await resposta.Content.ReadFromJsonAsync<Erro>(Json);

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.False(erro!.Success);
            Assert.Contains("não é permitida", Assert.Single(erro.Errors));
        }

        [Fact]
        public async Task OS_inexistente_responde_404()
        {
            var resposta = await _admin.GetAsync("/api/ordemservico/999999");

            Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        }

        [Fact]
        public async Task Validacao_do_corpo_responde_400_no_envelope_de_erro()
        {
            var resposta = await _admin.PostAsJsonAsync("/api/ordemservico", new { clienteId = 1, veiculoId = 10, descricaoProblema = "" });
            var erro = await resposta.Content.ReadFromJsonAsync<Erro>(Json);

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Contains("A descrição do problema é obrigatória.", erro!.Errors);
        }

        [Fact]
        public async Task Veiculo_de_outro_cliente_responde_400()
        {
            var resposta = await _admin.PostAsJsonAsync("/api/ordemservico",
                new { clienteId = 1, veiculoId = 20, descricaoProblema = "Teste" });

            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }

        [Fact]
        public async Task MechanicLtda_fora_do_ar_responde_503()
        {
            _factory.Gateway.ForaDoAr = true;

            var resposta = await _admin.PostAsJsonAsync("/api/ordemservico",
                new { clienteId = 1, veiculoId = 10, descricaoProblema = "Teste" });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
            _factory.Gateway.ForaDoAr = false;
        }

        [Fact]
        public async Task Cliente_consulta_a_propria_OS_e_nao_a_de_outro()
        {
            var minha = await AbrirAsync(clienteId: 1, veiculoId: 10);
            var deOutro = await AbrirAsync(clienteId: 2, veiculoId: 20);
            var cliente = _factory.ClienteComRole("Cliente", clienteId: 1);

            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/ordemservico/{minha.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/ordemservico/{minha.Id}/historico")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/ordemservico/cliente/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/ordemservico/{deOutro.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/ordemservico/{deOutro.Id}/historico")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/ordemservico/cliente/2")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/ordemservico")).StatusCode);
        }

        [Fact]
        public async Task Itens_sao_mantidos_enquanto_a_OS_esta_em_diagnostico()
        {
            var os = await AbrirAsync();
            var url = $"/api/ordem-servico/{os.Id}/itens";

            var criado = await (await _admin.PostAsJsonAsync(url,
                new { descricao = "Pastilha", tipo = "Peca", quantidade = 2, valorUnitario = 80m })).Content.ReadFromJsonAsync<Item>(Json);
            Assert.Equal(160m, criado!.ValorTotal);

            var atualizado = await (await _admin.PutAsJsonAsync($"{url}/{criado.Id}",
                new { descricao = "Pastilha", tipo = "Peca", quantidade = 1, valorUnitario = 80m })).Content.ReadFromJsonAsync<Item>(Json);
            Assert.Equal(80m, atualizado!.ValorTotal);
            Assert.Single((await _admin.GetFromJsonAsync<List<Item>>(url, Json))!);

            await PatchAsync(os.Id, "iniciar-diagnostico");
            await PatchAsync(os.Id, "aguardar-aprovacao");
            var bloqueado = await _admin.DeleteAsync($"{url}/{criado.Id}");
            Assert.Equal(HttpStatusCode.BadRequest, bloqueado.StatusCode);
        }

        [Fact]
        public async Task Atualiza_e_remove_a_OS()
        {
            var os = await AbrirAsync();

            var atualizada = await (await _admin.PutAsJsonAsync($"/api/ordemservico/{os.Id}",
                new { descricaoProblema = "Outro problema", valorTotalEstimado = 250m })).Content.ReadFromJsonAsync<Os>(Json);
            Assert.Equal(2, atualizada!.Status);

            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/ordemservico/{os.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/ordemservico/{os.Id}")).StatusCode);
        }
    }

    public class OrdemServicoApiComSagaTests : IClassFixture<OsServiceApiFactoryComSaga>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _admin;

        public OrdemServicoApiComSagaTests(OsServiceApiFactoryComSaga factory) => _admin = factory.ClienteComRole("Funcionario");

        private record Os(int Id, int Status, int? OrcamentoId, int? PagamentoId, DateTime? DataInicioExecucao,
                          DateTime? DataFimExecucao, List<JsonElement> Itens);
        private record Historico(int? StatusAnterior, int StatusNovo, string Origem, string? Motivo);

        private async Task<int> AbrirEDiagnosticarAsync(string descricao)
        {
            var aberta = await _admin.PostAsJsonAsync("/api/ordemservico", new { clienteId = 1, veiculoId = 10, descricaoProblema = descricao });
            var id = (await aberta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var diagnostico = await _admin.PatchAsync($"/api/ordemservico/{id}/iniciar-diagnostico", null);
            Assert.Equal(HttpStatusCode.OK, diagnostico.StatusCode);
            return id;
        }

        /// <summary>A Saga anda sozinha; espera o histórico chegar ao status esperado.</summary>
        private async Task<List<Historico>> EsperarStatusAsync(int id, int status, int ocorrencias = 1)
        {
            var limite = DateTime.UtcNow.AddSeconds(20);
            List<Historico> historico;
            do
            {
                historico = (await _admin.GetFromJsonAsync<List<Historico>>($"/api/ordemservico/{id}/historico", Json))!;
                if (historico.Count(h => h.StatusNovo == status) >= ocorrencias)
                    return historico;
                await Task.Delay(100);
            } while (DateTime.UtcNow < limite);

            Assert.Fail($"A OS {id} não chegou ao status {status}. Histórico: {string.Join(" > ", historico.Select(h => h.StatusNovo))}");
            return historico;
        }

        [Fact]
        public async Task Fluxo_completo_vai_de_recebida_a_finalizada()
        {
            var id = await AbrirEDiagnosticarAsync("Barulho no freio");

            var historico = await EsperarStatusAsync(id, 5);

            Assert.Equal([1, 2, 7, 3, 4, 5], historico.Select(h => h.StatusNovo));
            Assert.All(historico.Skip(2), h => Assert.Equal("Saga", h.Origem));

            var os = await _admin.GetFromJsonAsync<Os>($"/api/ordemservico/{id}", Json);
            Assert.Equal(1000 + id, os!.OrcamentoId);
            Assert.Equal(2000 + id, os.PagamentoId);
            Assert.Equal(2, os.Itens.Count);
            Assert.NotNull(os.DataInicioExecucao);
            Assert.NotNull(os.DataFimExecucao);

            // A entrega continua manual.
            Assert.Equal(HttpStatusCode.OK, (await _admin.PatchAsync($"/api/ordemservico/{id}/entregar", null)).StatusCode);
        }

        [Fact]
        public async Task Pagamento_recusado_nao_cancela_e_a_segunda_tentativa_leva_a_execucao()
        {
            var id = await AbrirEDiagnosticarAsync("Troca de óleo [falha-pagamento]");

            var historico = await EsperarStatusAsync(id, 5);

            Assert.Equal([1, 2, 7, 3, 4, 5], historico.Select(h => h.StatusNovo));
            Assert.StartsWith("Orçamento aprovado com o pagamento", historico[4].Motivo);
        }

        [Fact]
        public async Task Falha_na_execucao_estorna_o_pagamento_e_cancela_o_orcamento()
        {
            var id = await AbrirEDiagnosticarAsync("Motor falhando [falha-execucao]");

            var historico = await EsperarStatusAsync(id, 10);

            Assert.Equal([1, 2, 7, 3, 4, 9, 10], historico.Select(h => h.StatusNovo));
            Assert.Null((await _admin.GetFromJsonAsync<Os>($"/api/ordemservico/{id}", Json))!.DataFimExecucao);
        }

        [Fact]
        public async Task Orcamento_recusado_cancela_a_OS()
        {
            var id = await AbrirEDiagnosticarAsync("Pintura [recusar]");

            var historico = await EsperarStatusAsync(id, 10);

            Assert.Equal([1, 2, 7, 3, 10], historico.Select(h => h.StatusNovo));
        }

        [Fact]
        public async Task Falha_no_diagnostico_devolve_a_OS_para_recebida_e_permite_tentar_de_novo()
        {
            var id = await AbrirEDiagnosticarAsync("Ruído [falha-diagnostico]");

            var historico = await EsperarStatusAsync(id, 1, ocorrencias: 2);
            Assert.Equal([1, 2, 1], historico.Select(h => h.StatusNovo));

            Assert.Equal(HttpStatusCode.OK, (await _admin.PatchAsync($"/api/ordemservico/{id}/iniciar-diagnostico", null)).StatusCode);
            await EsperarStatusAsync(id, 1, ocorrencias: 3);
        }

        [Theory]
        [InlineData("aguardar-aprovacao")]
        [InlineData("aprovar")]
        [InlineData("recusar")]
        [InlineData("finalizar")]
        public async Task Transicoes_da_saga_respondem_409(string acao)
        {
            var aberta = await _admin.PostAsJsonAsync("/api/ordemservico", new { clienteId = 1, veiculoId = 10, descricaoProblema = "Teste" });
            var id = (await aberta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var resposta = await _admin.PatchAsync($"/api/ordemservico/{id}/{acao}", null);

            Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        }
    }
}
