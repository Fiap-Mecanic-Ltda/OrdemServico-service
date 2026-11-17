using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace OsService.Domain.Enums
{
    public static class StatusOrdemServicoExtensions
    {
        /// <summary>
        /// Nome de exibição do status (atributo Display), ex.: "Diagnóstico". É o texto usado
        /// nos e-mails ao cliente e nos painéis de monitoramento.
        /// </summary>
        public static string Descricao(this StatusOrdemServico status)
        {
            var campo = typeof(StatusOrdemServico).GetField(status.ToString());

            return campo?.GetCustomAttribute<DisplayAttribute>()?.Name ?? status.ToString();
        }

        /// <summary>Status em que a Saga terminou e a OS não muda mais sozinha.</summary>
        public static bool EhFinal(this StatusOrdemServico status) =>
            status is StatusOrdemServico.Entregue or StatusOrdemServico.Cancelada;
    }
}
