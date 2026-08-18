using AnimeCatalog.Models.AniList;

namespace AnimeCatalog.Services;

/// <summary>
/// Caches AniList enrichment for the lifetime of the app and collapses duplicate requests.
/// </summary>
/// <remarks>
/// <para>
/// Registered as scoped, which in Blazor WebAssembly means one instance for the whole app, so the
/// cache survives navigation between the catalog, franchise and anime pages.
/// </para>
/// <para>
/// Three properties matter here, because every call is made from the visitor's own browser against
/// a per-IP rate limit that is currently far below AniList's documented 90 req/min:
/// requests for the same id are collapsed into one in-flight task; ids are batched 50 at a time;
/// and HTTP calls are serialised so opening a 30-entry franchise cannot burst the limit.
/// Failures are cached briefly and surfaced as null rather than thrown, so a page never breaks
/// because AniList is unavailable.
/// </para>
/// </remarks>
public sealed class AniListEnrichmentService : IAniListEnrichmentService
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);

    // Short, so a transient outage does not lock the page out of enrichment for the whole session,
    // but long enough that a failing id is not retried on every re-render.
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    private readonly IAniListService _aniListService;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<int, CacheEntry> _cache = [];
    private readonly Dictionary<int, Task<AniListMedia?>> _inFlight = [];
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    /// <summary>AniList allows 30 requests a minute, so one every two seconds stays just inside it.</summary>
    public static readonly TimeSpan DefaultRequestSpacing = TimeSpan.FromMilliseconds(2100);

    private readonly TimeSpan _minRequestSpacing;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public AniListEnrichmentService(
        IAniListService aniListService,
        TimeProvider? timeProvider = null,
        TimeSpan? minRequestSpacing = null)
    {
        _aniListService = aniListService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _minRequestSpacing = minRequestSpacing ?? DefaultRequestSpacing;
    }

    public async Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default)
    {
        var results = await GetManyAsync([aniListId], cancellationToken);
        return results.GetValueOrDefault(aniListId);
    }

    public async Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(
        IReadOnlyCollection<int> aniListIds,
        CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<int, AniListMedia>();

        if (aniListIds.Count == 0)
        {
            return resolved;
        }

        var pending = new Dictionary<int, Task<AniListMedia?>>();
        var claimed = new Dictionary<int, TaskCompletionSource<AniListMedia?>>();
        var now = _timeProvider.GetUtcNow();

        foreach (var id in aniListIds.Distinct())
        {
            if (_cache.TryGetValue(id, out var cached) && cached.ExpiresAt > now)
            {
                if (cached.Media is not null)
                {
                    resolved[id] = cached.Media;
                }

                continue;
            }

            // Someone else is already fetching this id: ride along instead of issuing a second call.
            if (_inFlight.TryGetValue(id, out var existing))
            {
                pending[id] = existing;
                continue;
            }

            var completion = new TaskCompletionSource<AniListMedia?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[id] = completion.Task;
            pending[id] = completion.Task;
            claimed[id] = completion;
        }

        if (claimed.Count > 0)
        {
            await FetchAndPublishAsync(claimed, cancellationToken);
        }

        foreach (var (id, task) in pending)
        {
            var media = await task;
            if (media is not null)
            {
                resolved[id] = media;
            }
        }

        return resolved;
    }

    private async Task FetchAndPublishAsync(
        Dictionary<int, TaskCompletionSource<AniListMedia?>> claimed,
        CancellationToken cancellationToken)
    {
        var ids = claimed.Keys.ToList();
        var fetched = new Dictionary<int, AniListMedia>();
        Exception? failure = null;

        try
        {
            foreach (var chunk in Chunk(ids, AniListService.MaxBatchSize))
            {
                // Serialised so a large franchise issues its batches one after another rather than
                // firing every chunk at once into a rate limit we share with the whole page.
                await _requestGate.WaitAsync(cancellationToken);

                try
                {
                    await PaceAsync(cancellationToken);

                    var media = await _aniListService.GetEnrichedAnimeByIdsAsync(chunk, cancellationToken);
                    foreach (var item in media)
                    {
                        fetched[item.Id] = item;
                    }
                }
                finally
                {
                    _requestGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Navigating away mid-enrichment is routine. Drop the claims without caching a failure
            // so the next visit retries immediately.
            foreach (var (id, completion) in claimed)
            {
                _inFlight.Remove(id);
                completion.TrySetResult(null);
            }

            return;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var now = _timeProvider.GetUtcNow();

        // AniList returns batched media ordered by id, not in the order they were requested, so
        // results are matched by id throughout — never by position.
        foreach (var (id, completion) in claimed)
        {
            var media = fetched.GetValueOrDefault(id);

            // A successful call that simply did not include this id means AniList genuinely has
            // nothing here, so it is cached for the full window instead of retried in two minutes.
            var ttl = media is null && failure is not null ? FailureTtl : SuccessTtl;

            _cache[id] = new CacheEntry(media, now + ttl);
            _inFlight.Remove(id);
            completion.TrySetResult(media);
        }
    }

    /// <summary>
    /// Holds outgoing requests to AniList's documented ceiling.
    /// </summary>
    /// <remarks>
    /// A bulk walk of the relation graph issues dozens of batches back to back and would otherwise
    /// trip the 30/min limit part-way through, turning whole batches into cached failures and silently
    /// dropping results. Spacing them is cheaper than recovering from a 429, and it only applies to
    /// calls that actually reach the network — cache hits never get here.
    /// </remarks>
    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        if (_minRequestSpacing <= TimeSpan.Zero)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var wait = _nextRequestAt - now;

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _timeProvider, cancellationToken);
            now = _timeProvider.GetUtcNow();
        }

        _nextRequestAt = now + _minRequestSpacing;
    }

    private static IEnumerable<List<int>> Chunk(List<int> ids, int size)
    {
        for (var index = 0; index < ids.Count; index += size)
        {
            yield return ids.GetRange(index, Math.Min(size, ids.Count - index));
        }
    }

    private sealed record CacheEntry(AniListMedia? Media, DateTimeOffset ExpiresAt);
}
