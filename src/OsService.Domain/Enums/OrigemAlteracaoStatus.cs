using System.Text.Json.Serialization;

namespace OsService.Domain.Enums
{
    /// <summary>Quem provocou a mudança de status: uma pessoa pela API ou um passo da Saga.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrigemAlteracaoStatus
    {
        Usuario = 1,
        Saga = 2
    }
}
