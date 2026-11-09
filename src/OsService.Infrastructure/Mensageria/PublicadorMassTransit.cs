using MassTransit;
using MechanicLtda.Contratos;
using OsService.Application.Interfaces;
using OsService.Application.Saga;

namespace OsService.Infrastructure.Mensageria
{
    /// <summary>
    /// Publica pelo IPublishEndpoint do escopo. Com o bus outbox, a mensagem é gravada na
    /// tabela os.OutboxMessage pelo mesmo DbContext e só vai ao RabbitMQ depois do commit.
    /// </summary>
    public class PublicadorMassTransit : IPublicadorMensagens
    {
        private readonly IPublishEndpoint _publish;
        private readonly ISendEndpointProvider _send;

        public PublicadorMassTransit(IPublishEndpoint publish, ISendEndpointProvider send)
        {
            _publish = publish;
            _send    = send;
        }

        public Task PublicarAsync<T>(T mensagem, string? correlationIdRequisicao, CancellationToken ct = default) where T : class =>
            Entregar(_publish, _send, mensagem, mensagem.GetType(), correlationIdRequisicao, ct);

        /// <summary>Mensagem interna vai direto para a fila da Saga; mensagem do contrato vai para o exchange dela.</summary>
        internal static async Task Entregar(IPublishEndpoint publish, ISendEndpointProvider send, object mensagem, Type tipo,
                                            string? correlationIdRequisicao, CancellationToken ct)
        {
            var correlationId = CorrelationIdDe(mensagem);

            if (FilasSaga.EhInterna(tipo))
            {
                var saga = await send.GetSendEndpoint(FilasSaga.EnderecoSaga);
                await saga.Send(mensagem, tipo, s => CabecalhosSaga.Aplicar(s, correlationId, correlationIdRequisicao), ct);
            }
            else
            {
                await publish.Publish(mensagem, tipo, p => CabecalhosSaga.Aplicar(p, correlationId, correlationIdRequisicao), ct);
            }
        }

        public Task ConfirmarAsync(CancellationToken ct = default) => Task.CompletedTask;

        internal static Guid CorrelationIdDe(object mensagem) => mensagem switch
        {
            MensagemSaga m                     => m.CorrelationId,
            DiagnosticoSolicitado m            => m.CorrelationId,
            OrdemServicoCanceladaManualmente m => m.CorrelationId,
            _                                  => Guid.Empty,
        };
    }

    /// <summary>
    /// Transporte em memória (sem outbox): guarda as mensagens e só publica em ConfirmarAsync,
    /// depois que a OS foi gravada. Sem isso a Saga poderia ler a OS antes do commit.
    /// </summary>
    public class PublicadorAposSalvar : IPublicadorMensagens
    {
        private readonly IPublishEndpoint _publish;
        private readonly ISendEndpointProvider _send;
        private readonly List<(object Mensagem, string? CorrelationIdRequisicao)> _pendentes = [];

        public PublicadorAposSalvar(IPublishEndpoint publish, ISendEndpointProvider send)
        {
            _publish = publish;
            _send    = send;
        }

        public Task PublicarAsync<T>(T mensagem, string? correlationIdRequisicao, CancellationToken ct = default) where T : class
        {
            _pendentes.Add((mensagem, correlationIdRequisicao));
            return Task.CompletedTask;
        }

        public async Task ConfirmarAsync(CancellationToken ct = default)
        {
            var lote = _pendentes.ToList();
            _pendentes.Clear();

            foreach (var (mensagem, correlationIdRequisicao) in lote)
                await PublicadorMassTransit.Entregar(_publish, _send, mensagem, mensagem.GetType(), correlationIdRequisicao, ct);
        }
    }

    /// <summary>Saga desligada: nada é publicado (e o serviço nem conecta no RabbitMQ).</summary>
    public class PublicadorDesligado : IPublicadorMensagens
    {
        public Task PublicarAsync<T>(T mensagem, string? correlationIdRequisicao, CancellationToken ct = default) where T : class =>
            Task.CompletedTask;

        public Task ConfirmarAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
