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
}
