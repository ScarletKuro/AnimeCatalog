namespace AnimeCatalog.ViewModels;

/// <summary>
/// How the catalog overlay affects what is shown.
/// </summary>
/// <remarks>
/// One three-way choice rather than WatchNext's pair of selects. That page splits its equivalent in
/// two because its predicate is numeric and needs a control of its own; here the predicate is simply
/// "is it mine", so the cross-product has exactly three meaningful states and a second select would
/// render a nonsense fourth.
/// </remarks>
public enum CatalogHighlightFilter
{
    /// <summary>Everything, with cataloged titles highlighted.</summary>
    All,

    /// <summary>Only titles in the catalog.</summary>
    OnlyMine,

    /// <summary>Everything, but plays down what is not in the catalog instead of hiding it.</summary>
    DimOthers
}

/// <summary>
/// The airing week's client-side filters.
/// </summary>
/// <remarks>
/// These are applied to the loaded week in memory, never by re-querying. The whole window is already
/// on hand, and asking AniList again with a format_in would spend another five to seven paced
/// requests to get back a subset of what is already here. The archive does the opposite - see
/// AniListBrowseRequest - because paging means server-side filtering changes what lands on page one.
/// </remarks>
public sealed class AiringWeekFilters
{
    public string Format { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public bool HideAdult { get; set; } = true;

    /// <summary>Hides the TV_SHORT kids' filler that dominates a raw week.</summary>
    public bool HideShorts { get; set; }

    public CatalogHighlightFilter Catalog { get; set; } = CatalogHighlightFilter.All;

    public string Query { get; set; } = string.Empty;
}
