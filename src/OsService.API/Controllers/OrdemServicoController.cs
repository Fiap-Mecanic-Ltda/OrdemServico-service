using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OsService.API.ViewModels;
using OsService.Application.AppServices;
using OsService.Application.DTOs;
using OsService.Bootstrap.Authorization;

namespace OsService.API.Controllers
{
    /// <summary>
    /// Ordens de serviço. As rotas são as mesmas da API da Fase 3 (MechanicLtda), para o API
    /// Gateway e a coleção Postman continuarem valendo.
    /// </summary>
    [Route("api/[controller]")]
    public class OrdemServicoController : BaseApiController
    {
        private readonly IOrdemServicoAppService _app;

        public OrdemServicoController(IOrdemServicoAppService app) => _app = app;

        /// <summary>Lista as OS em andamento, das mais urgentes para as menos. [Admin]</summary>
        [HttpGet]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Listar(CancellationToken ct) =>
            Ok(await _app.ListarEmAndamentoAsync(ct));

        /// <summary>Tempo médio de execução das OS finalizadas. [Admin]</summary>
        [HttpGet("tempo-medio-execucao")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> TempoMedioExecucao(CancellationToken ct) =>
            Ok(await _app.ObterTempoMedioExecucaoAsync(ct));

        /// <summary>Consulta uma OS com status e itens. [Admin; Cliente só a própria]</summary>
        [HttpGet("{id:int}")]
        [Authorize(Roles = Roles.AdminOuCliente)]
        public async Task<IActionResult> Obter(int id, CancellationToken ct)
        {
            var ordem = await _app.ObterAsync(id, ct);

            return PodeAcessarCliente(ordem.ClienteId) ? Ok(ordem) : ClienteSemPermissao();
        }

        /// <summary>Histórico de status da OS, da abertura ao status atual. [Admin; Cliente só a própria]</summary>
        [HttpGet("{id:int}/historico")]
        [Authorize(Roles = Roles.AdminOuCliente)]
        public async Task<IActionResult> Historico(int id, CancellationToken ct)
        {
            var ordem = await _app.ObterAsync(id, ct);
            if (!PodeAcessarCliente(ordem.ClienteId))
                return ClienteSemPermissao();

            return Ok(await _app.ObterHistoricoAsync(id, ct));
        }

        /// <summary>OS de um cliente. [Admin; Cliente só o próprio clienteId]</summary>
        [HttpGet("cliente/{clienteId:int}")]
        [Authorize(Roles = Roles.AdminOuCliente)]
        public async Task<IActionResult> ObterPorCliente(int clienteId, CancellationToken ct)
        {
            if (!PodeAcessarCliente(clienteId))
                return ClienteSemPermissao();

            return Ok(await _app.ListarPorClienteAsync(clienteId, ct));
        }

        /// <summary>Abre uma OS (status 'Recebida'). Confere no MechanicLtda que o veículo é do cliente. [Admin]</summary>
        [HttpPost]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Abrir([FromBody] AbrirOrdemServicoViewModel model, CancellationToken ct) =>
            Ok(await _app.AbrirAsync(new AbrirOrdemServicoDto(model.ClienteId, model.VeiculoId,
                                                              model.DescricaoProblema, model.ValorTotalEstimado), ct));

        /// <summary>Atualiza descrição e valor estimado; com valor estimado, a OS recebida vai para diagnóstico. [Admin]</summary>
        [HttpPut("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarOrdemServicoViewModel model, CancellationToken ct) =>
            Ok(await _app.AtualizarAsync(id, new AtualizarOrdemServicoDto(model.DescricaoProblema, model.ValorTotalEstimado), ct));

        /// <summary>Recebida → Em Diagnóstico. [Admin]</summary>
        [HttpPatch("{id:int}/iniciar-diagnostico")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> IniciarDiagnostico(int id, CancellationToken ct) =>
            Ok(await _app.IniciarDiagnosticoAsync(id, ct));

        /// <summary>Em Diagnóstico → Aguardando Aprovação. Só com a Saga desligada. [Admin]</summary>
        [HttpPatch("{id:int}/aguardar-aprovacao")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> AguardarAprovacao(int id, CancellationToken ct) =>
            Ok(await _app.AguardarAprovacaoAsync(id, ct));

        /// <summary>Aguardando Aprovação → Em Execução. Só com a Saga desligada. [Admin]</summary>
        [HttpPatch("{id:int}/aprovar")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Aprovar(int id, CancellationToken ct) =>
            Ok(await _app.AprovarAsync(id, ct));

        /// <summary>Mesmo efeito de 'aprovar'; mantido por compatibilidade com a Fase 3. [Admin]</summary>
        [HttpPatch("{id:int}/iniciar-execucao")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> IniciarExecucao(int id, CancellationToken ct) =>
            Ok(await _app.AprovarAsync(id, ct));

        /// <summary>Aguardando Aprovação → Cancelada. Só com a Saga desligada. [Admin]</summary>
        [HttpPatch("{id:int}/recusar")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Recusar(int id, [FromBody] RecusaOrdemServicoViewModel? model, CancellationToken ct) =>
            Ok(await _app.RecusarAsync(id, model?.MotivoRecusa, ct));

        /// <summary>Em Execução → Finalizada. Só com a Saga desligada. [Admin]</summary>
        [HttpPatch("{id:int}/finalizar")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Finalizar(int id, CancellationToken ct) =>
            Ok(await _app.FinalizarAsync(id, ct));

        /// <summary>Finalizada → Entregue. [Admin]</summary>
        [HttpPatch("{id:int}/entregar")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Entregar(int id, CancellationToken ct) =>
            Ok(await _app.EntregarAsync(id, ct));

        /// <summary>Cancela a OS. Com a Saga ligada, depois do orçamento o cancelamento passa pela compensação. [Admin]</summary>
        [HttpPatch("{id:int}/cancelar")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Cancelar(int id, [FromBody] CancelamentoOrdemServicoViewModel? model, CancellationToken ct) =>
            Ok(await _app.CancelarAsync(id, model?.Motivo, ct));

        /// <summary>Remove uma OS que ainda não entrou na Saga (Recebida ou em Diagnóstico). [Admin]</summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = Roles.Admin)]
        public async Task<IActionResult> Remover(int id, CancellationToken ct)
        {
            await _app.RemoverAsync(id, ct);
            return NoContent();
        }
    }
}
