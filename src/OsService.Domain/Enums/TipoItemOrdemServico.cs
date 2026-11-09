using System.Text.Json.Serialization;

namespace OsService.Domain.Enums
{
    /// <summary>Tipo do item, com os mesmos nomes do contrato de mensagens (Peca, Insumo, Servico).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TipoItemOrdemServico
    {
        Peca = 1,
        Insumo = 2,
        Servico = 3
    }
}
