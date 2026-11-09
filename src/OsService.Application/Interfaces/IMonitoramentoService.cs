using OsService.Domain.Entities;
using OsService.Domain.Enums;

namespace OsService.Application.Interfaces
{
    /// <summary>Eventos de negócio para o New Relic (mesmos nomes de evento da Fase 3).</summary>
    public interface IMonitoramentoService
    {
        void RegistrarOrdemServicoCriada(OrdemServico ordemServico);
        void RegistrarMudancaStatus(OrdemServico ordemServico, StatusOrdemServico statusAnterior, TimeSpan tempoNoStatusAnterior);
        void RegistrarFalhaIntegracao(string integracao, string operacao, Exception excecao);
    }
}
