namespace OsService.Application.Interfaces
{
    /// <summary>
    /// Publica mensagens no broker. A mensagem só pode sair depois que a OS foi gravada:
    /// chame <see cref="ConfirmarAsync"/> logo depois do SalvarAsync do repositório.
    /// Com o outbox do SQL Server a mensagem já vai na transação e ConfirmarAsync não faz nada.
    /// </summary>
    public interface IPublicadorMensagens
    {
        Task PublicarAsync<T>(T mensagem, string? correlationIdRequisicao, CancellationToken ct = default) where T : class;

        Task ConfirmarAsync(CancellationToken ct = default);
    }
}
