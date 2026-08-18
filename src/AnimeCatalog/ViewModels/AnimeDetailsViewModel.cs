using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class AnimeDetailsViewModel
{
    public required AnimeEntry AnimeEntry { get; init; }
    public required CatalogEntry CatalogEntry { get; init; }
    public Franchise? Franchise { get; init; }

    /// <summary>
    /// Relations resolved against the local catalog: in-catalog targets carry their real title and a
    /// local link, the rest link out to AniList.
    /// </summary>
    public IReadOnlyList<RelationLinkViewModel> Relations { get; init; } = [];

    public string PrimaryTitle => AnimeEntry.TitleEnglish ?? AnimeEntry.TitleRomaji;
}
