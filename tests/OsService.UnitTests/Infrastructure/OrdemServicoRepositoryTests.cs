using Microsoft.EntityFrameworkCore;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using OsService.Infrastructure;
using OsService.Infrastructure.Repositories;

namespace OsService.UnitTests.Infrastructure
{
    public class OrdemServicoRepositoryTests
    {
        private static readonly DateTime Inicio = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

        private static OsServiceContext NovoContexto(string banco) =>
            new(new DbContextOptionsBuilder<OsServiceContext>().UseInMemoryDatabase(banco).Options);

        private static OrdemServico Executada(int clienteId, int minutos)
        {
            var os = OrdemServico.Abrir(clienteId, 1, "Revisão", null, Inicio, null);
            os.AlterarStatus(StatusOrdemServico.EmDiagnostico, Inicio, OrigemAlteracaoStatus.Usuario);
            os.AlterarStatus(StatusOrdemServico.AguardandoAprovacao, Inicio, OrigemAlteracaoStatus.Usuario);
            os.AlterarStatus(StatusOrdemServico.EmExecucao, Inicio, OrigemAlteracaoStatus.Usuario);
            os.AlterarStatus(StatusOrdemServico.Finalizada, Inicio.AddMinutes(minutos), OrigemAlteracaoStatus.Usuario);
            return os;
        }

        [Fact]
        public async Task Salva_e_le_a_OS_com_itens_e_historico()
        {
            var banco = Guid.NewGuid().ToString();
            int id;

            await using (var contexto = NovoContexto(banco))
            {
                var repositorio = new OrdemServicoRepository(contexto);
                var os = OrdemServico.Abrir(1, 2, "Freio", 100m, Inicio, "req-1");
                os.AdicionarItem("Pastilha", TipoItemOrdemServico.Peca, 2, 80m, 5, null, Inicio);
                os.AlterarStatus(StatusOrdemServico.EmDiagnostico, Inicio.AddMinutes(5), OrigemAlteracaoStatus.Usuario);

                await repositorio.AdicionarAsync(os);
                await repositorio.SalvarAsync();
                id = os.Id;
            }

            await using (var contexto = NovoContexto(banco))
            {
                var lida = await new OrdemServicoRepository(contexto).ObterPorIdAsync(id);

                Assert.NotNull(lida);
                Assert.Equal(StatusOrdemServico.EmDiagnostico, lida.Status);
                Assert.Equal(160m, lida.ValorTotalItens);
                Assert.Equal(2, lida.Historico.Count);
            }
        }

        [Fact]
        public async Task Filtra_por_cliente_e_remove()
        {
            await using var contexto = NovoContexto(Guid.NewGuid().ToString());
            var repositorio = new OrdemServicoRepository(contexto);
            var doCliente   = OrdemServico.Abrir(1, 2, "A", null, Inicio, null);
            await repositorio.AdicionarAsync(doCliente);
            await repositorio.AdicionarAsync(OrdemServico.Abrir(3, 4, "B", null, Inicio, null));
            await repositorio.SalvarAsync();

            Assert.Single(await repositorio.ObterPorClienteAsync(1));
            Assert.Equal(2, (await repositorio.ObterTodasAsync()).Count);

            repositorio.Remover(doCliente);
            await repositorio.SalvarAsync();

            Assert.Null(await repositorio.ObterPorIdAsync(doCliente.Id));
        }

        [Fact]
        public async Task Tempo_medio_considera_so_as_OS_executadas()
        {
            await using var contexto = NovoContexto(Guid.NewGuid().ToString());
            var repositorio = new OrdemServicoRepository(contexto);
            await repositorio.AdicionarAsync(Executada(1, 60));
            await repositorio.AdicionarAsync(Executada(1, 120));
            await repositorio.AdicionarAsync(OrdemServico.Abrir(1, 1, "Aberta", null, Inicio, null));
            await repositorio.SalvarAsync();

            var (quantidade, media) = await repositorio.ObterTempoMedioExecucaoAsync();

            Assert.Equal(2, quantidade);
            Assert.Equal(90, media);
        }

        [Fact]
        public async Task Tempo_medio_sem_OS_finalizadas_e_zero()
        {
            await using var contexto = NovoContexto(Guid.NewGuid().ToString());

            Assert.Equal((0, 0d), await new OrdemServicoRepository(contexto).ObterTempoMedioExecucaoAsync());
        }
    }
}
