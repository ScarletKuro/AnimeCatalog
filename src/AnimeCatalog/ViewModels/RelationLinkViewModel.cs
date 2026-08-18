using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// A resolved AniList relation: either an entry already in the local catalog, or an outbound link.
/// </summary>
public sealed class RelationLinkViewModel
{
    public required string RelationType { get; init; }

    public int TargetAniListId { get; init; }

    /// <summary>Set when the relation target exists in <c>anime_entries</c>.</summary>
    public long? LocalAnimeEntryId { get; init; }

    public required string Title { get; init; }

    public string? CoverUrl { get; init; }

    public string? Format { get; init; }

    public int? SeasonYear { get; init; }

    public CatalogStatus? CatalogStatus { get; init; }

    /// <summary>AniList's own canonical URL for the target, when the live payload supplied one.</summary>
    public string? SiteUrl { get; init; }

    /// <summary>
    /// True once the target is positively known to be a non-music anime. Only confirmed relations are
    /// rendered.
    /// </summary>
    /// <remarks>
    /// An in-catalog target is confirmed immediately — <c>anime_entries</c> only holds anime. An
    /// out-of-catalog target stays unconfirmed until AniList supplies the node's type and format,
    /// because <c>anime_relations</c> stores neither. Unconfirmed is the fail-safe state: a manga can
    /// never leak into an anime-only catalog, at the cost of hiding a relation AniList cannot classify.
    /// </remarks>
    public bool IsConfirmedAnime { get; init; }

    public bool IsInCatalog => LocalAnimeEntryId is not null;

    public string DisplayLabel => RelationType.ToDisplayLabel();

    public string Href => LocalAnimeEntryId is not null
        ? $"anime/{LocalAnimeEntryId}"
        : SiteUrl ?? $"https://anilist.co/anime/{TargetAniListId}";
}
