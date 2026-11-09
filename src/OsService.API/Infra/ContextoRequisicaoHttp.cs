using OsService.API.Middlewares;
using OsService.Application.Interfaces;

namespace OsService.API.Infra
{
    public class ContextoRequisicaoHttp : IContextoRequisicao
    {
        private readonly IHttpContextAccessor _accessor;

        public ContextoRequisicaoHttp(IHttpContextAccessor accessor) => _accessor = accessor;

        public string? CorrelationId =>
            _accessor.HttpContext?.Items[CorrelationIdMiddleware.HeaderName] as string;

        public string? Authorization =>
            _accessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
    }
}
