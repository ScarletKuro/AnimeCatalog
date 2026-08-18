using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.Transfer;

/// <summary>
/// Portable catalog backup. Deliberately carries no database row IDs: anime are keyed by AniList
/// ID and franchises by slug, so a file exported from one Supabase instance can be merged into a
/// rebuilt or entirely different one.
/// </summary>
public sealed class CatalogExportFile
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; }

    [JsonPropertyName("franchises")]
    public List<CatalogExportFranchise> Franchises { get; set; } = [];

    [JsonPropertyName("entries")]
    public List<CatalogExportEntry> Entries { get; set; } = [];
}

public sealed class CatalogExportFranchise
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class CatalogExportEntry
{
    [JsonPropertyName("anilistId")]
    public int AniListId { get; set; }

    [JsonPropertyName("franchiseSlug")]
    public string? FranchiseSlug { get; set; }

    [JsonPropertyName("titleRomaji")]
    public string TitleRomaji { get; set; } = string.Empty;

    [JsonPropertyName("titleEnglish")]
    public string? TitleEnglish { get; set; }

    [JsonPropertyName("titleNative")]
    public string? TitleNative { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("season")]
    public string? Season { get; set; }

    [JsonPropertyName("seasonYear")]
    public int? SeasonYear { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("startDate")]
    public DateOnly? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateOnly? EndDate { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    [JsonPropertyName("partNumber")]
    public int? PartNumber { get; set; }

    [JsonPropertyName("displayOrder")]
    public int DisplayOrder { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public decimal? Score { get; set; }

    [JsonPropertyName("episodesWatched")]
    public int EpisodesWatched { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("startedAt")]
    public DateOnly? StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateOnly? CompletedAt { get; set; }

    [JsonPropertyName("relations")]
    public List<CatalogExportRelation> Relations { get; set; } = [];
}

public sealed class CatalogExportRelation
{
    [JsonPropertyName("targetAnilistId")]
    public int TargetAniListId { get; set; }

    [JsonPropertyName("relationType")]
    public string RelationType { get; set; } = string.Empty;
}
