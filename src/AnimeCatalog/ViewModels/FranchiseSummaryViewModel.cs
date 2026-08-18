namespace AnimeCatalog.ViewModels;

public sealed class FranchiseSummaryViewModel
{
    public long? FranchiseId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? CoverUrl { get; init; }
    public int EntryCount { get; init; }
    public int CompletedCount { get; init; }
    public decimal? AverageScore { get; init; }
    public bool IsWatching { get; init; }
    public IReadOnlyList<AnimeListItemViewModel> Entries { get; init; } = [];
    public IReadOnlyList<AnimeListItemViewModel> VisibleEntries { get; init; } = [];
}
