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
}
