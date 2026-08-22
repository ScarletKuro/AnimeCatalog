using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>One tile in the archive grid.</summary>
public sealed record SeasonArchiveEntryViewModel
{
    public required int AniListId { get; init; }

    public required string Title { get; init; }

    public string? CoverUrl { get; init; }

    public string? Format { get; init; }

    /// <summary>AniList's own season for this title, which is what a whole-year browse groups by.</summary>
    public string? Season { get; init; }

    public int? SeasonYear { get; init; }

    public int? Episodes { get; init; }

    /// <summary>AniList's community score, 0-100. Rendered as a footer fact, not as the owner's score.</summary>
    public int? CommunityScore { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string? SiteUrl { get; init; }

    public string? Status { get; init; }

    public CatalogOverlayItem? Catalog { get; init; }

    public bool IsCataloged => Catalog is not null;

    /// <summary>Cataloged titles route inward; everything else opens AniList in a new tab.</summary>
    public string? Href => Catalog is not null ? $"anime/{Catalog.AnimeEntryId}" : SiteUrl;

    public bool IsExternalHref => Catalog is null;

    public string? Subtitle => MediaDisplayFormatter.FormatAndYear(Format, SeasonYear);

    public CatalogStatus? CatalogStatus => Catalog?.Status;

    /// <summary>The owner's own score, which is what PosterCard.Score means.</summary>
    public decimal? OwnerScore => Catalog?.Score;

    public int? ProgressPercent => Catalog?.ProgressPercent;

    public int? EpisodesWatched => Catalog?.EpisodesWatched;

    public string CommunityScoreLabel => CommunityScore is null ? "Not rated yet" : $"AniList {CommunityScore}";
}

/// <summary>
/// One band of a whole-year browse. A single-season browse produces exactly one of these with a null
/// <see cref="Season"/>, so the page can render both shapes from the same list.
/// </summary>
public sealed record SeasonArchiveGroup(string? Season, IReadOnlyList<SeasonArchiveEntryViewModel> Entries)
{
    /// <summary>Null for a single-season browse, where the panel heading already names the season.</summary>
    public string? Heading => Season is null ? null : MediaDisplayFormatter.SeasonLabel(Season);
}

/// <summary>One season's - or one whole year's - worth of archive results.</summary>
public sealed record SeasonArchiveViewModel
{
    public required int Year { get; init; }

    /// <summary>A MediaSeason, or <see cref="AnimeSeasonCalendar.WholeYear"/>.</summary>
    public required string Season { get; init; }

    public required IReadOnlyList<SeasonArchiveEntryViewModel> Entries { get; init; }

    /// <summary>
    /// The entries banded for display. One group for a single season; up to five for a whole year,
    /// in broadcast order with titles AniList left unseasoned last.
    /// </summary>
    public IReadOnlyList<SeasonArchiveGroup> Groups { get; init; } = [];

    /// <summary>How many arrived before the catalog filter, so "no matches" reads differently from "empty season".</summary>
    public int LoadedCount { get; init; }

    public int CatalogedCount { get; init; }

    public bool IsWholeYear => AnimeSeasonCalendar.IsWholeYear(Season);

    /// <summary>A whole-year browse is titled by the year alone - the groups name the seasons.</summary>
    public string Heading => IsWholeYear
        ? Year.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{MediaDisplayFormatter.SeasonLabel(Season)} {Year}";
}
