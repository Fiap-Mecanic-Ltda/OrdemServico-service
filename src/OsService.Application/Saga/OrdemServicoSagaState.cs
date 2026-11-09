using MassTransit;

namespace OsService.Application.Saga
{
    /// <summary>
    /// Estado da Saga de uma OS (tabela os.SagaOrdemServico). O status visível da OS continua
    /// na própria OrdemServico; aqui fica o que a Saga precisa lembrar entre uma mensagem e
    /// outra. A linha é apagada quando a Saga termina.
    /// </summary>
    public class OrdemServicoSagaState : SagaStateMachineInstance
    {
        /// <summary>Mesmo valor de OrdemServico.CorrelationId.</summary>
        public Guid CorrelationId { get; set; }

        public string EstadoAtual { get; set; } = string.Empty;

        public int OrdemServicoId { get; set; }
        public int ClienteId { get; set; }
        public int VeiculoId { get; set; }

        /// <summary>Vão no GerarOrcamento: o Billing envia o e-mail sem consultar o cadastro de clientes.</summary>
        public string NomeCliente { get; set; } = string.Empty;
        public string EmailCliente { get; set; } = string.Empty;

        public int? OrcamentoId { get; set; }
        public decimal? ValorOrcamento { get; set; }
        public int? PagamentoId { get; set; }

        /// <summary>Lidos pelo verificador de prazos; nulos quando não há prazo correndo.</summary>
        public DateTime? PrazoDiagnostico { get; set; }
        public DateTime? PrazoAprovacao { get; set; }

        /// <summary>Confirmações de compensação ainda pendentes.</summary>
        public bool AguardandoOrcamentoCancelado { get; set; }
        public bool AguardandoPagamentoEstornado { get; set; }
        public string? MotivoCompensacao { get; set; }

        /// <summary>X-Correlation-Id da requisição HTTP que iniciou a Saga (rastreio no New Relic).</summary>
        public string? CorrelationIdRequisicao { get; set; }

        public DateTime DataInicio { get; set; }
        public DateTime? DataAtualizacao { get; set; }
    }
}
