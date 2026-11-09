using Microsoft.Extensions.Logging;
using OsService.Application.Interfaces;
using OsService.Domain.Entities;
using OsService.Domain.Enums;
using NewRelicAgent = NewRelic.Api.Agent.NewRelic;

namespace OsService.Infrastructure.Monitoramento
{
    /// <summary>
    /// Eventos de negócio no New Relic, com os mesmos nomes da Fase 3 (OrdemServicoEvento e
    /// FalhaIntegracao) para os painéis e alertas existentes continuarem valendo; o atributo
    /// "servico" diferencia o OS Service do MechanicLtda. Sem o agente carregado (testes,
    /// execução local) a chamada não faz nada, e o evento também sai no log.
    /// </summary>
    public class NewRelicMonitoramentoService : IMonitoramentoService
    {
        private const string Servico = "os-service";
        private const int TamanhoMaximoMensagem = 255;

        private readonly ILogger<NewRelicMonitoramentoService> _logger;

        public NewRelicMonitoramentoService(ILogger<NewRelicMonitoramentoService> logger) => _logger = logger;

        public void RegistrarOrdemServicoCriada(OrdemServico ordemServico) =>
            Registrar("OrdemServicoEvento", new Dictionary<string, object>
            {
                ["tipo"]            = "Criada",
                ["ordemServicoId"]  = ordemServico.Id,
                ["correlationId"]   = ordemServico.CorrelationId.ToString(),
                ["status"]          = ordemServico.Status.ToString(),
                ["statusDescricao"] = ordemServico.Status.Descricao(),
            });

        public void RegistrarMudancaStatus(OrdemServico ordemServico, StatusOrdemServico statusAnterior, TimeSpan tempoNoStatusAnterior) =>
            Registrar("OrdemServicoEvento", new Dictionary<string, object>
            {
                ["tipo"]                    = "MudancaStatus",
                ["ordemServicoId"]          = ordemServico.Id,
                ["correlationId"]           = ordemServico.CorrelationId.ToString(),
                ["statusAnterior"]          = statusAnterior.ToString(),
                ["statusAnteriorDescricao"] = statusAnterior.Descricao(),
                ["statusNovo"]              = ordemServico.Status.ToString(),
                ["statusNovoDescricao"]     = ordemServico.Status.Descricao(),
                ["minutosNoStatusAnterior"] = Math.Round(tempoNoStatusAnterior.TotalMinutes, 2),
            });

        public void RegistrarFalhaIntegracao(string integracao, string operacao, Exception excecao) =>
            Registrar("FalhaIntegracao", new Dictionary<string, object>
            {
                ["integracao"] = integracao,
                ["operacao"]   = operacao,
                ["erro"]       = excecao.GetType().Name,
                ["mensagem"]   = Limitar(excecao.Message),
            });

        private void Registrar(string tipoEvento, Dictionary<string, object> atributos)
        {
            atributos["servico"] = Servico;

            try
            {
                NewRelicAgent.RecordCustomEvent(tipoEvento, atributos);
            }
            catch (Exception ex)
            {
                // Monitoramento nunca derruba a operação de negócio.
                _logger.LogDebug(ex, "Não foi possível enviar o evento {TipoEvento} ao New Relic.", tipoEvento);
            }

            _logger.LogInformation("Evento {TipoEvento}: {@Atributos}", tipoEvento, atributos);
        }

        private static string Limitar(string texto) =>
            texto.Length <= TamanhoMaximoMensagem ? texto : texto[..TamanhoMaximoMensagem];
    }
}
