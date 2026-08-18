using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.Supabase;

public sealed class AnimeRelationRow
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("source_anime_id")]
    public long SourceAnimeId { get; set; }

    [JsonPropertyName("target_anilist_id")]
    public int TargetAniListId { get; set; }

    [JsonPropertyName("relation_type")]
    public string RelationType { get; set; } = string.Empty;
}
