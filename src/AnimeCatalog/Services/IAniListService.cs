using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.Services;

public interface IAniListService
{
    Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default);

    Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the wide field set used to enrich the detail pages (synopsis, banner, genres, tags,
    /// studios, community scores, relation nodes with titles and covers).
    /// </summary>
    Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched form of <see cref="GetEnrichedAnimeByIdAsync"/>. At most
    /// <see cref="AniListService.MaxBatchSize"/> ids per call; media AniList does not know about are
    /// simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of AniList's airing schedule for a time window, sorted by airing time so a partial
    /// multi-page read is always a chronological prefix.
    /// </summary>
    /// <remarks>
    /// The window is half-open in intent but AniList's <c>airingAt_greater</c> is strictly greater,
    /// so <paramref name="windowStartInclusive"/> is sent as one second earlier to keep an episode
    /// airing exactly on the boundary.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either bound falls outside the 32-bit unix range AniList's Int arguments accept - i.e. past
    /// 2038-01-19. The schedule model reads airingAt as a long, but the filter arguments are Int.
    /// </exception>
    Task<AniListPageResult<AniListAiringSchedule>> GetAiringSchedulesAsync(
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        int page,
        int perPage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a season or year browse. Filters left unset on <paramref name="request"/> are
    /// omitted from the GraphQL variables entirely, which makes GraphQL skip the argument.
    /// </summary>
    Task<AniListPageResult<AniListMedia>> BrowseMediaAsync(
        AniListBrowseRequest request,
        int page,
        int perPage,
        CancellationToken cancellationToken = default);
}
