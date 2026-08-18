using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.Services;

/// <summary>
/// Cached, de-duplicated access to AniList's wide media field set.
/// </summary>
public interface IAniListEnrichmentService
{
    /// <summary>
    /// Returns enrichment for one AniList id, or null when AniList has no such media or the
    /// request failed. Never throws for transport or API errors — callers degrade gracefully.
    /// </summary>
    Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns enrichment for many ids in as few requests as possible. Ids AniList does not return
    /// are simply absent from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(
        IReadOnlyCollection<int> aniListIds,
        CancellationToken cancellationToken = default);
}
