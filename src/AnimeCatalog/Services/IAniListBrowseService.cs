using AnimeCatalog.Models.AniList;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

/// <summary>
/// Cached, paced, de-duplicated access to AniList's paged browse and airing-schedule queries.
/// </summary>
/// <remarks>
/// Unlike <see cref="IAniListEnrichmentService"/>, this DOES throw. Enrichment is decoration, so a
/// null degrades into a card with fewer facts on it; here the AniList data IS the page, and handing
/// back an empty week because the API is down is indistinguishable from "nothing airs this week".
/// Callers must handle <see cref="Infrastructure.AniListUnavailableException"/>.
/// </remarks>
public interface IAniListBrowseService
{
    /// <summary>One page of a season or year browse. Cached per request signature and page.</summary>
    Task<AniListPageResult<AniListMedia>> GetBrowsePageAsync(
        AniListBrowseRequest request,
        int page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every airing schedule in a window, walked page by page and reported after each.
    /// </summary>
    /// <remarks>
    /// Throws only if the FIRST page fails. A failure later returns what did load with
    /// <see cref="AiringScheduleLoad.IsComplete"/> false and a degraded message, because half a week
    /// on screen is worth more to the visitor than an error card.
    /// </remarks>
    Task<AiringScheduleLoad> GetAiringSchedulesAsync(
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        IProgress<AiringScheduleLoad>? progress = null,
        CancellationToken cancellationToken = default);
}
