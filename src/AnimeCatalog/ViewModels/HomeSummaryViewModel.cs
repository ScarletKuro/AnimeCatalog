using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// Whole-catalog aggregates for the home page, computed purely from Supabase data so the page is
/// complete and correct on first paint. This is <see cref="FranchiseStatsViewModel"/> one level up:
/// the same status breakdown and episode rollups, but across every entry rather than one franchise.
/// AniList-dependent extras (banner art, airing countdowns) stay on the page, not in here.
/// </summary>
public sealed class HomeSummaryViewModel
{
    public int TotalEntries { get; init; }

    /// <summary>
    /// Real franchises only. <see cref="Services.FranchiseService.BuildCatalog"/> also emits a
    /// singleton pseudo-franchise per ungrouped anime, and counting those as franchises would make
    /// this number meaningless.
    /// </summary>
    public int FranchiseCount { get; init; }

    /// <summary>Entries that belong to no franchise, counted separately from <see cref="FranchiseCount"/>.</summary>
    public int StandaloneCount { get; init; }

    /// <summary>Real franchises where every entry is completed.</summary>
    public int CompletedFranchises { get; init; }

    /// <summary>Counts for all five statuses, including zeroes, in enum order.</summary>
    public IReadOnlyList<StatusCount> StatusBreakdown { get; init; } = [];

    public int EpisodesWatched { get; init; }

    /// <summary>Sum of known episode counts; entries with a null count contribute nothing.</summary>
    public int EpisodesTotal { get; init; }

    /// <summary>True when at least one entry has no episode count, so totals read as "n+".</summary>
    public bool HasUnknownEpisodeCounts { get; init; }

    public decimal? AverageScore { get; init; }

    public int ScoredCount { get; init; }

    public decimal? HighestScore { get; init; }

    /// <summary>Non-empty buckets only, highest score first. See <see cref="ScoreBucket"/>.</summary>
    public IReadOnlyList<ScoreBucket> ScoreDistribution { get; init; } = [];

    public int CompletedThisYear { get; init; }

    public int CompletedLast30Days { get; init; }

    /// <summary>The watching entry touched most recently, or null when nothing is in progress.</summary>
    public AnimeListItemViewModel? Spotlight { get; init; }

    /// <summary>The remaining watching entries, excluding <see cref="Spotlight"/>.</summary>
    public IReadOnlyList<AnimeListItemViewModel> ContinueWatching { get; init; } = [];

    public IReadOnlyList<AnimeListItemViewModel> RecentlyCompleted { get; init; } = [];

    public IReadOnlyList<AnimeListItemViewModel> HighestRated { get; init; } = [];

    public IReadOnlyList<AnimeListItemViewModel> RecentlyAdded { get; init; } = [];

    /// <summary>Real franchises only, so every item has a slug to link to.</summary>
    public IReadOnlyList<FranchiseSummaryViewModel> TopFranchises { get; init; } = [];

    public int CompletedEntries => CountFor(CatalogStatus.Completed);

    public int WatchingEntries => CountFor(CatalogStatus.Watching);

    public int CompletionPercent => TotalEntries <= 0
        ? 0
        : (int)Math.Round(CompletedEntries / (double)TotalEntries * 100);

    public int CountFor(CatalogStatus status) =>
        StatusBreakdown.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
}

/// <summary>
/// One bar of the score histogram. <paramref name="Score"/> is the whole-number bucket a score falls
/// into by flooring it, so 8.0 through 8.9 all land in bucket 8 and only a perfect 10 lands in 10.
/// </summary>
public sealed record ScoreBucket(int Score, int Count);
