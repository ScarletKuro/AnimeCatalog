using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// AniList rollups across every entry in a franchise. Always safe to render partially: a franchise
/// where only some entries resolved still produces correct chips for the ones that did.
/// </summary>
public sealed class FranchiseEnrichmentViewModel
{
    public IReadOnlyDictionary<int, AniListMedia> ByAniListId { get; init; } = new Dictionary<int, AniListMedia>();

    public int EntryCount { get; init; }

    public int LoadedCount { get; init; }

    public bool IsPartial => LoadedCount < EntryCount;

    public bool HasAny => LoadedCount > 0;

    /// <summary>Genres across the franchise, most common first.</summary>
    public IReadOnlyList<LabelCount> Genres { get; init; } = [];

    /// <summary>Mean AniList community score (0-100) over entries that have one.</summary>
    public int? AniListAverageScore { get; init; }

    /// <summary>Banner of the most popular entry — the flagship art, not an OVA's.</summary>
    public string? BannerUrl { get; init; }
}

public sealed record LabelCount(string Label, int Count);
