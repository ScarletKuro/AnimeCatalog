using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// What the admin add page learns about an AniList id in one lookup: whether it is already catalogued,
/// a ready draft if it is not, and its anime relations marked against the catalog.
/// </summary>
/// <remarks>
/// Selecting an already-added anime used to be a dead end — an error and nothing else — which hid the
/// fact that a sequel was missing. Carrying both outcomes in one result keeps that to a single AniList
/// request, which matters against a 30/min rate limit.
/// </remarks>
public sealed class AniListInspectionViewModel
{
    public int AniListId { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>The catalog entry for this AniList id, when it is already added.</summary>
    public AnimeEntry? ExistingEntry { get; init; }

    /// <summary>The franchise of <see cref="ExistingEntry"/>, so a new season can inherit it.</summary>
    public Franchise? ExistingFranchise { get; init; }

    /// <summary>Populated only when the anime is not already in the catalog.</summary>
    public AnimeEditorModel? Draft { get; init; }

    /// <summary>Anime relations only — manga, novels, OSTs and vague link types are excluded.</summary>
    public IReadOnlyList<RelatedAnimeSuggestion> Relations { get; init; } = [];

    public bool IsAlreadyInCatalog => ExistingEntry is not null;

    public IReadOnlyList<RelatedAnimeSuggestion> MissingRelations =>
        Relations.Where(relation => !relation.IsInCatalog).ToList();
}
