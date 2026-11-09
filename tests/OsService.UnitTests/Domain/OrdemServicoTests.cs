using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Domain.Exceptions;

namespace OsService.UnitTests.Domain
{
    public class OrdemServicoTests
    {
        private static readonly DateTime T0 = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

        private static OrdemServico NovaOrdem() =>
            OrdemServico.Abrir(clienteId: 7, veiculoId: 3, "Barulho no freio", 350m, T0, "req-1");

        private static OrdemServico OrdemEm(StatusOrdemServico destino)
        {
            var os = NovaOrdem();
            var caminho = destino switch
            {
                StatusOrdemServico.Recebida            => [],
                StatusOrdemServico.EmDiagnostico       => [StatusOrdemServico.EmDiagnostico],
                StatusOrdemServico.AguardandoOrcamento => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoOrcamento],
                StatusOrdemServico.AguardandoAprovacao => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao],
                StatusOrdemServico.EmExecucao          => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao],
                StatusOrdemServico.Finalizada          => [StatusOrdemServico.EmDiagnostico, StatusOrdemServico.AguardandoAprovacao, StatusOrdemServico.EmExecucao, StatusOrdemServico.Finalizada],
                _ => new StatusOrdemServico[] { }
            };

            var t = T0;
            foreach (var status in caminho)
            {
                t = t.AddMinutes(10);
                os.AlterarStatus(status, t, OrigemAlteracaoStatus.Usuario);
            }
            return os;
        }

        [Fact]
        public void Abrir_cria_OS_recebida_com_saga_e_historico_inicial()
        {
            var os = NovaOrdem();

            Assert.Equal(StatusOrdemServico.Recebida, os.Status);
            Assert.NotEqual(Guid.Empty, os.CorrelationId);
            Assert.Equal(T0, os.DataCriacao);
            Assert.Equal("Barulho no freio", os.DescricaoProblema);

            var linha = Assert.Single(os.Historico);
            Assert.Null(linha.StatusAnterior);
            Assert.Equal(StatusOrdemServico.Recebida, linha.StatusNovo);
            Assert.Equal("req-1", linha.CorrelationId);
        }

        [Theory]
        [InlineData(0, 1, "desc", "ClienteId")]
        [InlineData(1, 0, "desc", "VeiculoId")]
        [InlineData(1, 1, " ", "descrição")]
        public void Abrir_valida_dados_obrigatorios(int clienteId, int veiculoId, string descricao, string trecho)
        {
            var erro = Assert.Throws<RegraDeNegocioException>(() =>
                OrdemServico.Abrir(clienteId, veiculoId, descricao, null, T0, null));

            Assert.Contains(trecho, erro.Message);
        }

        [Fact]
        public void Abrir_recusa_descricao_longa_e_valor_negativo()
        {
            Assert.Throws<RegraDeNegocioException>(() =>
                OrdemServico.Abrir(1, 1, new string('a', OrdemServico.TamanhoMaximoDescricao + 1), null, T0, null));
            Assert.Throws<RegraDeNegocioException>(() =>
                OrdemServico.Abrir(1, 1, "ok", -1m, T0, null));
        }

        [Fact]
        public void AlterarStatus_valida_transicao_registra_historico_e_mede_tempo()
        {
            var os = NovaOrdem();

            var (anterior, tempo) = os.AlterarStatus(StatusOrdemServico.EmDiagnostico, T0.AddMinutes(30),
                                                     OrigemAlteracaoStatus.Saga, "diagnóstico iniciado", "req-2");

            Assert.Equal(StatusOrdemServico.Recebida, anterior);
            Assert.Equal(TimeSpan.FromMinutes(30), tempo);
            Assert.Equal(StatusOrdemServico.EmDiagnostico, os.Status);
            Assert.Equal(T0.AddMinutes(30), os.DataAlteracaoStatus);

            var ultima = os.Historico.Last();
            Assert.Equal(StatusOrdemServico.Recebida, ultima.StatusAnterior);
            Assert.Equal(OrigemAlteracaoStatus.Saga, ultima.Origem);
            Assert.Equal("diagnóstico iniciado", ultima.Motivo);
        }

        [Fact]
        public void AlterarStatus_recusa_transicao_nao_permitida()
        {
            var os = NovaOrdem();

            var erro = Assert.Throws<RegraDeNegocioException>(() =>
                os.AlterarStatus(StatusOrdemServico.Finalizada, T0, OrigemAlteracaoStatus.Usuario));

            Assert.Contains("não é permitida", erro.Message);
            Assert.Single(os.Historico);
        }

        [Fact]
        public void AlterarStatus_com_relogio_atrasado_nao_gera_tempo_negativo()
        {
            var os = NovaOrdem();

            var (_, tempo) = os.AlterarStatus(StatusOrdemServico.EmDiagnostico, T0.AddMinutes(-5), OrigemAlteracaoStatus.Usuario);

            Assert.Equal(TimeSpan.Zero, tempo);
        }

        [Fact]
        public void Execucao_e_finalizacao_gravam_datas_e_tempo_de_execucao()
        {
            var os = OrdemEm(StatusOrdemServico.Finalizada);

            Assert.Equal(T0.AddMinutes(30), os.DataInicioExecucao);
            Assert.Equal(T0.AddMinutes(40), os.DataFimExecucao);
            Assert.Equal(TimeSpan.FromMinutes(10), os.TempoExecucao);
            Assert.Equal(5, os.Historico.Count);
        }

        [Fact]
        public void Motivo_longo_e_truncado_no_historico()
        {
            var os = NovaOrdem();

            os.AlterarStatus(StatusOrdemServico.Cancelada, T0, OrigemAlteracaoStatus.Usuario, new string('m', 800));

            Assert.Equal(500, os.Historico.Last().Motivo!.Length);
        }

        [Fact]
        public void AtualizarDados_so_antes_do_orcamento()
        {
            var os = OrdemEm(StatusOrdemServico.EmDiagnostico);
            os.AtualizarDados("Nova descrição", 500m, T0.AddHours(1));

            Assert.Equal("Nova descrição", os.DescricaoProblema);
            Assert.Equal(500m, os.ValorTotalEstimado);
            Assert.Equal(T0.AddHours(1), os.DataModificacao);

            var emAprovacao = OrdemEm(StatusOrdemServico.AguardandoAprovacao);
            Assert.Throws<RegraDeNegocioException>(() => emAprovacao.AtualizarDados("x", null, T0));
        }

        [Fact]
        public void Itens_podem_ser_adicionados_atualizados_e_removidos_no_diagnostico()
        {
            var os = OrdemEm(StatusOrdemServico.EmDiagnostico);

            var item = os.AdicionarItem("Pastilha", TipoItemOrdemServico.Peca, 2, 89.90m, 15, null, T0);
            Assert.Equal(179.80m, item.ValorTotal);
            Assert.Equal(179.80m, os.ValorTotalItens);

            os.AtualizarItem(item.Id, "Pastilha cerâmica", TipoItemOrdemServico.Peca, 1, 120m, 15, null, T0);
            Assert.Equal(120m, os.ValorTotalItens);
            Assert.Equal("Pastilha cerâmica", os.ObterItem(item.Id).Descricao);

            os.RemoverItem(item.Id, T0);
            Assert.Empty(os.Itens);
        }

        [Fact]
        public void Itens_nao_mudam_depois_do_diagnostico()
        {
            var os = OrdemEm(StatusOrdemServico.AguardandoAprovacao);

            Assert.Throws<RegraDeNegocioException>(() =>
                os.AdicionarItem("x", TipoItemOrdemServico.Insumo, 1, 1m, null, null, T0));
        }

        [Theory]
        [InlineData("", TipoItemOrdemServico.Peca, 1, 1)]
        [InlineData("ok", (TipoItemOrdemServico)99, 1, 1)]
        [InlineData("ok", TipoItemOrdemServico.Peca, 0, 1)]
        [InlineData("ok", TipoItemOrdemServico.Peca, 1, -1)]
        public void Item_invalido_e_recusado(string descricao, TipoItemOrdemServico tipo, int quantidade, decimal valor)
        {
            var os = NovaOrdem();

            Assert.Throws<RegraDeNegocioException>(() =>
                os.AdicionarItem(descricao, tipo, quantidade, valor, null, null, T0));
        }

        [Fact]
        public void ObterItem_inexistente_lanca_nao_encontrado()
        {
            Assert.Throws<RecursoNaoEncontradoException>(() => NovaOrdem().ObterItem(42));
        }

        [Fact]
        public void SubstituirItens_troca_a_lista_inteira()
        {
            var os = OrdemEm(StatusOrdemServico.EmDiagnostico);
            os.AdicionarItem("antigo", TipoItemOrdemServico.Insumo, 1, 10m, null, null, T0);

            os.SubstituirItens(
            [
                ("Pastilha", TipoItemOrdemServico.Peca, 2, 50m, 15, null),
                ("Troca", TipoItemOrdemServico.Servico, 1, 120m, null, 3),
            ], T0);

            Assert.Equal(2, os.Itens.Count);
            Assert.Equal(220m, os.ValorTotalItens);
        }

        [Fact]
        public void Referencias_de_orcamento_e_pagamento_sao_guardadas()
        {
            var os = NovaOrdem();
            os.RegistrarOrcamento(10);
            os.RegistrarPagamento(20);

            Assert.Equal(10, os.OrcamentoId);
            Assert.Equal(20, os.PagamentoId);
        }
    }
}
