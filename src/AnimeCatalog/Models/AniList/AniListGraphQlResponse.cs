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

    /// <summary>
    /// AniList mirrors the HTTP status here, and it also sends this on some HTTP 200 responses -
    /// the "temporarily disabled" notice arrives that way - so the unavailability check reads it
    /// rather than trusting the transport status alone.
    /// </summary>
    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
