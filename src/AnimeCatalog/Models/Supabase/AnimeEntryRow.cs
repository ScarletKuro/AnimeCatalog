using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.Supabase;

public sealed class AnimeEntryRow
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("anilist_id")]
    public int AniListId { get; set; }

    [JsonPropertyName("franchise_id")]
    public long? FranchiseId { get; set; }

    [JsonPropertyName("title_romaji")]
    public string TitleRomaji { get; set; } = string.Empty;

    [JsonPropertyName("title_english")]
    public string? TitleEnglish { get; set; }

    [JsonPropertyName("title_native")]
    public string? TitleNative { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("season_year")]
    public int? SeasonYear { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("start_date")]
    public DateOnly? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public DateOnly? EndDate { get; set; }

    [JsonPropertyName("season_number")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("part_number")]
    public int? PartNumber { get; set; }

    [JsonPropertyName("display_order")]
    public int DisplayOrder { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
