namespace OsService.Application.Options
{
    /// <summary>
    /// Saga:Habilitada = false (padrão até Billing e Execução estarem no ar): as transições de
    /// aprovação, execução e finalização são feitas à mão pela API, como na Fase 3.
    /// Com true, essas transições passam a vir só por eventos e os endpoints manuais
    /// respondem 409.
    /// </summary>
    public class SagaOptions
    {
        public const string Secao = "Saga";

        public bool Habilitada { get; set; }
    }
}
