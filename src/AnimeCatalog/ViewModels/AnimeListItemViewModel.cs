using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

public sealed class AnimeListItemViewModel
{
    public required AnimeEntry AnimeEntry { get; init; }
    public required CatalogEntry CatalogEntry { get; init; }
    public Franchise? Franchise { get; init; }
    public IReadOnlyList<AnimeRelation> Relations { get; init; } = [];

    public string PrimaryTitle => AnimeEntry.TitleEnglish ?? AnimeEntry.TitleRomaji;

    public string SecondaryTitle => AnimeEntry.TitleEnglish is null ? AnimeEntry.TitleRomaji : AnimeEntry.TitleRomaji;
}
