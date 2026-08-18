using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// AniList-sourced detail for one anime, held alongside the Supabase-backed
/// <see cref="AnimeDetailsViewModel"/> so the page renders with or without it.
/// </summary>
public sealed class AnimeEnrichmentViewModel
{
    public required AniListMedia Media { get; init; }

    /// <summary>Sanitized once, here — never in a render loop.</summary>
    public SanitizedDescription Description { get; init; } = SanitizedDescription.Empty;

    /// <summary>Non-spoiler tags, most relevant first.</summary>
    public IReadOnlyList<AniListMediaTag> Tags { get; init; } = [];

    /// <summary>Spoiler tags, kept separate so the page can hide them behind a reveal.</summary>
    public IReadOnlyList<AniListMediaTag> SpoilerTags { get; init; } = [];

    public IReadOnlyList<AniListStudio> MainStudios { get; init; } = [];

    public IReadOnlyList<AniListStudio> Producers { get; init; } = [];

    /// <summary>All-time rankings first; these are the ones worth showing.</summary>
    public IReadOnlyList<AniListRanking> Rankings { get; init; } = [];

    public int? TotalRuntimeMinutes { get; init; }

    public string? BannerUrl => Media.BannerImage;

    public string? SiteUrl => Media.SiteUrl;

    public bool HasDescription => Description.HasContent;
}
