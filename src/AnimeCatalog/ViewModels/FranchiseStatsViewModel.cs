using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// Franchise aggregates computed purely from Supabase data, so they render on first paint and stay
/// correct when AniList is unreachable. AniList-dependent rollups live in
/// <see cref="FranchiseEnrichmentViewModel"/>.
/// </summary>
public sealed class FranchiseStatsViewModel
{
    public int EntryCount { get; init; }

    /// <summary>Counts for all five statuses, including zeroes, in enum order.</summary>
    public IReadOnlyList<StatusCount> StatusBreakdown { get; init; } = [];

    public int EpisodesWatched { get; init; }

    /// <summary>Sum of known episode counts; entries with a null count contribute nothing.</summary>
    public int EpisodesTotal { get; init; }

    /// <summary>True when at least one entry has no episode count, so totals read as "n+".</summary>
    public bool HasUnknownEpisodeCounts { get; init; }

    public int CompletedCount { get; init; }

    public int ScoredCount { get; init; }

    public decimal? AverageScore { get; init; }

    public decimal? HighestScore { get; init; }

    public decimal? LowestScore { get; init; }

    public int? FirstYear { get; init; }

    public int? LastYear { get; init; }

    public DateOnly? FirstStartedAt { get; init; }

    public DateOnly? LastCompletedAt { get; init; }

    public bool IsWatching { get; init; }

    public int CompletionPercent => EntryCount <= 0
        ? 0
        : (int)Math.Round(CompletedCount / (double)EntryCount * 100);

    public string? YearSpan => (FirstYear, LastYear) switch
    {
        (null, _) => null,
        (int first, int last) when first == last => first.ToString(),
        (int first, int last) => $"{first} – {last}",
        _ => FirstYear?.ToString()
    };
}

public sealed record StatusCount(CatalogStatus Status, int Count)
{
    public string Label => Status.ToDisplayLabel();

    public string ApiValue => Status.ToApiValue();
}
