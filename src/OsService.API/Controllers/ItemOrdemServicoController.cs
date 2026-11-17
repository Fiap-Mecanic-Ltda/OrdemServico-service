using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OsService.API.ViewModels;
using OsService.Application.AppServices;
using OsService.Application.DTOs;
using OsService.Bootstrap.Authorization;

namespace OsService.API.Controllers
{
    /// <summary>Itens da OS. Só podem mudar enquanto a OS está 'Recebida' ou em 'Diagnóstico'. [Admin]</summary>
    [Route("api/ordem-servico/{ordemServicoId:int}/itens")]
    [Authorize(Roles = Roles.Admin)]
    public class ItemOrdemServicoController : BaseApiController
    {
        private readonly IOrdemServicoAppService _app;

        public ItemOrdemServicoController(IOrdemServicoAppService app) => _app = app;

        [HttpGet]
        public async Task<IActionResult> Listar(int ordemServicoId, CancellationToken ct) =>
            Ok(await _app.ListarItensAsync(ordemServicoId, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obter(int ordemServicoId, int id, CancellationToken ct) =>
            Ok(await _app.ObterItemAsync(ordemServicoId, id, ct));

        [HttpPost]
        public async Task<IActionResult> Adicionar(int ordemServicoId, [FromBody] ItemOrdemServicoViewModel model, CancellationToken ct) =>
            Ok(await _app.AdicionarItemAsync(ordemServicoId, Dto(model), ct));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Atualizar(int ordemServicoId, int id, [FromBody] ItemOrdemServicoViewModel model, CancellationToken ct) =>
            Ok(await _app.AtualizarItemAsync(ordemServicoId, id, Dto(model), ct));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Remover(int ordemServicoId, int id, CancellationToken ct)
        {
            await _app.RemoverItemAsync(ordemServicoId, id, ct);
            return NoContent();
        }

        private static ItemOrdemServicoInputDto Dto(ItemOrdemServicoViewModel m) =>
            new(m.Descricao, m.Tipo, m.Quantidade, m.ValorUnitario, m.EstoqueId, m.ServicoOficinaId);
    }
}
