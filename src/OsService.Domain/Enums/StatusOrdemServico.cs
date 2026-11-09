using System.ComponentModel.DataAnnotations;

namespace OsService.Domain.Enums
{
    /// <summary>
    /// Status da Ordem de Serviço. Os valores 1 a 6 são os mesmos da API da Fase 3 (não
    /// mudam, para não quebrar clientes); 7 a 10 entram com a Saga. A tabela completa está em
    /// docs/contratos-eventos.md.
    /// </summary>
    public enum StatusOrdemServico
    {
        [Display(Name = "Recebida")]
        Recebida = 1,
        [Display(Name = "Diagnóstico")]
        EmDiagnostico = 2,
        [Display(Name = "Aguardando Aprovação")]
        AguardandoAprovacao = 3,
        [Display(Name = "Execução")]
        EmExecucao = 4,
        [Display(Name = "Finalizada")]
        Finalizada = 5,
        [Display(Name = "Entregue")]
        Entregue = 6,
        [Display(Name = "Aguardando Orçamento")]
        AguardandoOrcamento = 7,
        [Display(Name = "Aguardando Pagamento")]
        AguardandoPagamento = 8,
        [Display(Name = "Compensando")]
        Compensando = 9,
        [Display(Name = "Cancelada")]
        Cancelada = 10
    }
}
