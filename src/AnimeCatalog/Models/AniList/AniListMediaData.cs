using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.AniList;

public sealed class AniListMediaData
{
    [JsonPropertyName("Media")]
    public AniListMedia? Media { get; set; }

    [JsonPropertyName("Page")]
    public AniListMediaPage? Page { get; set; }
}

public sealed class AniListMediaPage
{
    [JsonPropertyName("media")]
    public List<AniListMedia> Media { get; set; } = [];

    // Requested only by the calendar and browse queries; the search, details and enrichment
    // documents leave these null or empty, exactly as AniListMedia's own field set does.
    [JsonPropertyName("pageInfo")]
    public AniListPageInfo? PageInfo { get; set; }

    [JsonPropertyName("airingSchedules")]
    public List<AniListAiringSchedule> AiringSchedules { get; set; } = [];
}
