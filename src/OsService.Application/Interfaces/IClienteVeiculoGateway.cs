namespace OsService.Application.Interfaces
{
    public record ClienteResumo(int Id, string Nome, string Email, string? Telefone = null);

    public record VeiculoResumo(int Id, int ClienteId, string? Placa,
                                string? Marca = null, string? Modelo = null, int? Ano = null);

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
