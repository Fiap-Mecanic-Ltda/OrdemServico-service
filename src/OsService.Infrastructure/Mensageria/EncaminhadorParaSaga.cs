using MassTransit;
using MechanicLtda.Contratos;
using OsService.Application.Saga;

namespace OsService.Infrastructure.Mensageria
{
    /// <summary>
    /// Lê o evento da fila própria (os-&lt;evento&gt;) e o envia para a fila da Saga, mantendo os
    /// cabeçalhos de correlação. Ver <see cref="FilasSaga"/>.
    /// </summary>
    public class EncaminhadorParaSaga<T> : IConsumer<T> where T : MensagemSaga
    {
        public async Task Consume(ConsumeContext<T> context)
        {
            var destino = await context.GetSendEndpoint(FilasSaga.EnderecoSaga);
            var requisicao = context.Headers.Get<string>(CabecalhosSaga.CorrelationIdRequisicao);

            await destino.Send(context.Message, s =>
            {
                s.CorrelationId = context.Message.CorrelationId;
                s.Headers.Set(CabecalhosSaga.CorrelationId, context.Message.CorrelationId.ToString());
                if (!string.IsNullOrWhiteSpace(requisicao))
                    s.Headers.Set(CabecalhosSaga.CorrelationIdRequisicao, requisicao);
            }, context.CancellationToken);
        }
    }
}
