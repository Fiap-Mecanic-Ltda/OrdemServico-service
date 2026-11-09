namespace OsService.Application.Interfaces
{
    public record ClienteResumo(int Id, string Nome, string Email);

    public record VeiculoResumo(int Id, int ClienteId, string? Placa);

    /// <summary>
    /// Consulta cliente e veículo no serviço que é dono desses dados (MechanicLtda), por REST.
    /// Nunca pelo banco dele. Retorna null quando o recurso não existe.
    /// </summary>
    public interface IClienteVeiculoGateway
    {
        Task<ClienteResumo?> ObterClienteAsync(int clienteId, CancellationToken ct = default);
        Task<VeiculoResumo?> ObterVeiculoAsync(int veiculoId, CancellationToken ct = default);
    }
}
