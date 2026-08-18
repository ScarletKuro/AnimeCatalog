using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class RelatedAnimeSuggestion
{
    public int AniListId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public string? Format { get; init; }
    public int? SeasonYear { get; init; }
    public string RelationType { get; init; } = string.Empty;

    /// <summary>Set when this relation is already an entry in the catalog.</summary>
    public long? LocalAnimeEntryId { get; init; }

    public bool IsInCatalog => LocalAnimeEntryId is not null;

    public string DisplayLabel => RelationType.ToDisplayLabel();
}
