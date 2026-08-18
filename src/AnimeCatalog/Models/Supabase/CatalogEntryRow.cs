using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.Supabase;

public sealed class CatalogEntryRow
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("anime_entry_id")]
    public long AnimeEntryId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("episodes_watched")]
    public int EpisodesWatched { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("started_at")]
    public DateOnly? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateOnly? CompletedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
