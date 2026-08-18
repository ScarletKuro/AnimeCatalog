namespace AnimeCatalog.ViewModels;

/// <summary>
/// Franchise entries bucketed by release year. Built from Supabase data only, so the timeline works
/// with AniList unavailable.
/// </summary>
public sealed class FranchiseTimelineGroup
{
    /// <summary>Null for entries with no known year; these sort last.</summary>
    public int? Year { get; init; }

    public IReadOnlyList<AnimeListItemViewModel> Entries { get; init; } = [];

    public string Label => Year?.ToString() ?? "Unknown";
}
