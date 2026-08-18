using System.Text.Json.Serialization;

namespace AnimeCatalog.Models;

public sealed class PostgrestError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Unknown PostgREST error.";
}
