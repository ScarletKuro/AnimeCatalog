using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;

namespace AnimeCatalog.ViewModels;

/// <summary>
/// What the owner's catalog says about one AniList id.
/// </summary>
public sealed record CatalogOverlayItem(
    long AnimeEntryId,
    int AniListId,
    CatalogStatus Status,
    int EpisodesWatched,
    decimal? Score,
    int? Episodes)
{
    /// <summary>
    /// Null rather than 0% when the episode count is unknown: an empty bar would claim no progress
    /// where the truth is that nobody knows the total. Same rule as the home page's poster tiles.
    /// </summary>
    public int? ProgressPercent => Episodes is > 0
        ? Math.Clamp((int)Math.Round(Math.Min(EpisodesWatched, Episodes.Value) / (double)Episodes.Value * 100), 0, 100)
        : null;
}

/// <summary>
/// An AniList-id-keyed view of the catalog, for decorating pages whose primary data is AniList's.
/// </summary>
/// <remarks>
/// <see cref="State"/> rather than an exception is the whole point: the calendar's AniList half is
/// public and has to render whether or not Supabase is configured, reachable, or readable by this
/// visitor. A caller checks <see cref="IsDecorating"/> to decide whether to show badges at all, and
/// reads <see cref="State"/> to decide whether that absence deserves an explanation.
/// </remarks>
public sealed record CatalogOverlay(
    IReadOnlyDictionary<int, CatalogOverlayItem> ByAniListId,
    CatalogAccessState State)
{
    public static CatalogOverlay Empty(CatalogAccessState state = CatalogAccessState.Available) =>
        new(new Dictionary<int, CatalogOverlayItem>(), state);

    public CatalogOverlayItem? Find(int aniListId) => ByAniListId.GetValueOrDefault(aniListId);

    /// <summary>Whether badges should be rendered at all.</summary>
    public bool IsDecorating => State == CatalogAccessState.Available;

    /// <summary>
    /// Whether the missing highlighting is worth explaining to the visitor. A private catalog is
    /// worth a line; an unconfigured Supabase is not - there is simply no catalog to compare
    /// against, and nagging about it on every load of a self-hosted instance would be noise.
    /// </summary>
    public bool ShouldExplainAbsence => State is CatalogAccessState.Private or CatalogAccessState.Error;
}
