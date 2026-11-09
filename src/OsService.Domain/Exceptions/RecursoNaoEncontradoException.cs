namespace OsService.Domain.Exceptions
{
    /// <summary>OS, item ou recurso externo inexistente. Vira HTTP 404.</summary>
    public class RecursoNaoEncontradoException : Exception
    {
        public RecursoNaoEncontradoException(string mensagem) : base(mensagem) { }
    }
}
