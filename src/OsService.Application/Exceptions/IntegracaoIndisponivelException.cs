namespace OsService.Application.Exceptions
{
    /// <summary>Serviço externo fora do ar ou respondendo erro. Vira HTTP 503.</summary>
    public class IntegracaoIndisponivelException : Exception
    {
        public IntegracaoIndisponivelException(string mensagem, Exception? inner = null) : base(mensagem, inner) { }
    }

    /// <summary>Operação manual bloqueada porque a Saga está habilitada. Vira HTTP 409.</summary>
    public class OperacaoControladaPelaSagaException : Exception
    {
        public OperacaoControladaPelaSagaException(string operacao)
            : base($"A operação '{operacao}' é feita pela Saga e não pode ser executada manualmente com a Saga habilitada.") { }
    }
}
