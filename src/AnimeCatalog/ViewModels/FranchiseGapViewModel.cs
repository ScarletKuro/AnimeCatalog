using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// One anime reachable from something you have watched that is not in the catalog.
/// </summary>
public sealed class MissingAnimeViewModel
{
    public required AniListMedia Media { get; init; }

    public int AniListId => Media.Id;

    public string Title => Media.Title.English ?? Media.Title.Romaji ?? $"AniList #{Media.Id}";

    /// <summary>How this was reached, e.g. SEQUEL. Relative to <see cref="DiscoveredFrom"/>.</summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// The title this was reached from. Often not something you watched — Darker than Black's season
    /// two is three hops out — so naming it keeps the explanation honest.
    /// </summary>
    public string? DiscoveredFrom { get; init; }

    /// <summary>
    /// AniList community score, 0-100, or null when nothing has rated it yet.
    /// </summary>
    /// <remarks>
    /// averageScore is AniList's weighted score and the better signal; meanScore is the plain
    /// arithmetic mean. In practice they agree almost always (across the Attack on Titan cluster they
    /// differ on three entries, by one point), so the mean only serves as a fallback when a title has
    /// no weighted score.
    /// </remarks>
    public int? Score => Media.AverageScore ?? Media.MeanScore;

    public bool IsReleased => !string.Equals(Media.Status, "NOT_YET_RELEASED", StringComparison.OrdinalIgnoreCase);

    public string DisplayLabel => RelationType.ToDisplayLabel();

    public string SiteUrl => Media.SiteUrl ?? $"https://anilist.co/anime/{Media.Id}";
}

/// <summary>
/// A connected franchise: everything AniList links together, split into what you have and what you
/// are missing.
/// </summary>
public sealed class FranchiseGapGroupViewModel
{
    public required string Title { get; init; }

    /// <summary>Set when the watched members share a local franchise, so the page can link to it.</summary>
    public string? FranchiseSlug { get; init; }

    public int OwnedCount { get; init; }

    public int TotalCount { get; init; }

    public IReadOnlyList<MissingAnimeViewModel> Missing { get; init; } = [];

    /// <summary>Drives group ordering: the best thing you are missing here.</summary>
    public int? BestScore => Missing.Max(item => item.Score);
}

/// <summary>
/// The outcome of a scan. Rendered while still running, so every field must be meaningful mid-walk.
/// </summary>
public sealed class FranchiseGapScanViewModel
{
    public IReadOnlyList<FranchiseGapGroupViewModel> Groups { get; init; } = [];

    /// <summary>Titles fetched from AniList so far — the honest progress signal, since the frontier grows.</summary>
    public int ScannedCount { get; init; }

    public int MissingCount => Groups.Sum(group => group.Missing.Count);

    /// <summary>True when the node cap stopped the walk before the graph was exhausted.</summary>
    public bool WasTruncated { get; init; }
}
