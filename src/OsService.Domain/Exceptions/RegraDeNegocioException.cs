namespace OsService.Domain.Exceptions
{
    /// <summary>Operação que fere uma regra da OS (ex.: transição de status inválida). Vira HTTP 400.</summary>
    public class RegraDeNegocioException : Exception
    {
        public RegraDeNegocioException(string mensagem) : base(mensagem) { }
    }
}
