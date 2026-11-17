using MechanicLtda.Contratos;
using System.Text.RegularExpressions;

namespace OsService.Application.Saga
{
    /// <summary>
    /// Filas do OS Service no RabbitMQ.
    ///
    /// Cada evento dos outros serviços chega numa fila própria, ligada ao exchange da mensagem
    /// (ex.: os-orcamento-aprovado ← MechanicLtda.Contratos:OrcamentoAprovado), como o contrato
    /// define. Como cada uma dessas filas recebe um único tipo, o JSON pode chegar sem nenhum
    /// cabeçalho do MassTransit. Um encaminhador repassa a mensagem, já tipada, para a fila da
    /// Saga, que processa uma OS por vez.
    /// </summary>
    public static partial class FilasSaga
    {
        public const string Saga = "os-ordem-servico-saga";

        public static readonly Uri EnderecoSaga = new($"queue:{Saga}");

        /// <summary>Eventos que a Saga recebe de Billing e Execução.</summary>
        public static readonly IReadOnlyList<Type> EventosExternos =
        [
            typeof(DiagnosticoConcluido), typeof(DiagnosticoFalhou),
            typeof(ExecucaoIniciada), typeof(ExecucaoFinalizada), typeof(ExecucaoFalhou),
            typeof(OrcamentoGerado), typeof(OrcamentoFalhou), typeof(OrcamentoAprovado), typeof(OrcamentoRecusado),
            typeof(PagamentoConfirmado), typeof(PagamentoFalhou), typeof(OrcamentoCancelado), typeof(PagamentoEstornado),
        ];

        /// <summary>os-&lt;mensagem-em-kebab&gt;, ex.: os-orcamento-aprovado.</summary>
        public static string FilaDoEvento(Type evento) => "os-" + Maiuscula().Replace(evento.Name, "-$1").ToLowerInvariant();

        /// <summary>Mensagens internas vão direto para a fila da Saga; as do contrato são publicadas no exchange.</summary>
        public static bool EhInterna(Type mensagem) => mensagem.Namespace == typeof(DiagnosticoSolicitado).Namespace;

        [GeneratedRegex("(?<!^)([A-Z])")]
        private static partial Regex Maiuscula();
    }
}
