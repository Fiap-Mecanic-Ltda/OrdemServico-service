namespace OsService.Application.Options
{
    /// <summary>
    /// Saga:Habilitada = false (padrão até Billing e Execução estarem no ar): as transições de
    /// aprovação, execução e finalização são feitas à mão pela API, como na Fase 3, e o
    /// serviço não conecta no RabbitMQ.
    /// Com true, essas transições passam a vir só por eventos e os endpoints manuais
    /// respondem 409.
    /// </summary>
    public class SagaOptions
    {
        public const string Secao = "Saga";

        public bool Habilitada { get; set; }

        /// <summary>Tempo para a Execução responder ao IniciarDiagnostico.</summary>
        public double PrazoDiagnosticoHoras { get; set; } = 48;

        /// <summary>Prazo de aprovação do orçamento quando o Billing não informa a validade.</summary>
        public double PrazoAprovacaoHoras { get; set; } = 72;

        /// <summary>De quanto em quanto tempo os prazos vencidos são procurados.</summary>
        public int IntervaloVerificacaoPrazosSegundos { get; set; } = 60;
    }
}
