namespace AnimeCatalog.Models;

public sealed class AnimeEntry
{
    public long Id { get; set; }
    public int AniListId { get; set; }
    public long? FranchiseId { get; set; }
    public string TitleRomaji { get; set; } = string.Empty;
    public string? TitleEnglish { get; set; }
    public string? TitleNative { get; set; }
    public string? CoverUrl { get; set; }
    public string? Format { get; set; }
    public string? Season { get; set; }
    public int? SeasonYear { get; set; }
    public int? Episodes { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? SeasonNumber { get; set; }
    public int? PartNumber { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
