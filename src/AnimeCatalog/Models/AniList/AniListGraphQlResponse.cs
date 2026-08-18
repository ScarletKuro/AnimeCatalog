using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.AniList;

public sealed class AniListGraphQlResponse<TData>
{
    [JsonPropertyName("data")]
    public TData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<AniListGraphQlError>? Errors { get; set; }
}

public sealed class AniListGraphQlError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
