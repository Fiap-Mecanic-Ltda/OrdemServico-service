using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OsService.Bootstrap.Authorization;

namespace OsService.API.Controllers
{
    [ApiController]
    [Authorize]
    public abstract class BaseApiController : ControllerBase
    {
        /// <summary>
        /// Posse do recurso: Administrador e Funcionário acessam qualquer cliente; o token de
        /// cliente (emitido pela Lambda de CPF) só acessa o próprio clienteId, que vem no claim.
        /// </summary>
        protected bool PodeAcessarCliente(int clienteId)
        {
            if (User.IsInRole(Roles.Administrador) || User.IsInRole(Roles.Funcionario))
                return true;

            return User.FindFirst("clienteId")?.Value == clienteId.ToString();
        }

        protected ActionResult ClienteSemPermissao() =>
            StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                errors  = new[] { "O cliente autenticado só pode consultar os próprios dados." }
            });
    }
}
