using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

/// <summary>
/// Caches and paces the calendar's paged AniList reads.
/// </summary>
/// <remarks>
/// <para>
/// Registered as scoped, which in Blazor WebAssembly means one instance for the whole app, so paging
/// a week back and then forward again costs nothing the second time. That matters more here than
/// anywhere else in the app: a cold week is five to seven requests spaced 2.1 seconds apart, so
/// re-fetching one the visitor already looked at would cost ten to fifteen seconds.
/// </para>
/// <para>
/// Requests go through the shared <see cref="AniListRequestPacer"/> rather than a private gate, so a
/// calendar load and a home-page enrichment burst queue behind each other instead of racing into a
/// 429. Results are deliberately never written into the enrichment cache - they come from the
/// narrower CalendarFields fragment, and a half-populated AniListMedia reaching the details page or
/// the franchise-gap walk would fail silently.
/// </para>
/// </remarks>
public sealed class AniListBrowseService : IAniListBrowseService
{
    /// <summary>
    /// Ceiling on the pages one airing window may cost.
    /// </summary>
    /// <remarks>
    /// A week was observed at five to seven pages, so ten leaves headroom while capping the worst
    /// case at about twenty-one seconds. Hitting it sets <see cref="AiringScheduleLoad.WasTruncated"/>
    /// rather than quietly returning a short week.
    /// </remarks>
    public const int MaxAiringPages = 10;

    /// <summary>
    /// Absolute stop on a browse walk - two thousand entries, far past any real season. This is a
    /// backstop against a filter combination that somehow never terminates, not a real limit.
    /// </summary>
    public const int MaxBrowsePages = 40;

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A window entirely in the past cannot change, so it is held far longer. Re-reading a finished
    /// week would otherwise cost five to seven paced requests for a guaranteed-identical answer.
    /// </summary>
    private static readonly TimeSpan HistoricalTtl = TimeSpan.FromHours(12);

    /// <summary>Matches the enrichment cache, so a brief outage is not remembered all session.</summary>
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    private readonly IAniListService _aniListService;
    private readonly AniListRequestPacer _requestPacer;
    private readonly TimeProvider _timeProvider;

    private readonly Dictionary<string, CacheEntry> _cache = [];
    private readonly Dictionary<string, Task<CachedPage>> _inFlight = [];

    public AniListBrowseService(
        IAniListService aniListService,
        AniListRequestPacer requestPacer,
        TimeProvider? timeProvider = null)
    {
        _aniListService = aniListService;
        _requestPacer = requestPacer;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AniListPageResult<AniListMedia>> GetBrowsePageAsync(
        AniListBrowseRequest request,
        int page,
        CancellationToken cancellationToken = default)
    {
        var key = $"{request.CacheSignature()}|p{page}";

        var cached = await ReadThroughAsync(
            key,
            IsHistoricalSeason(request) ? HistoricalTtl : SuccessTtl,
            async token =>
            {
                var result = await _aniListService.BrowseMediaAsync(request, page, AniListService.MaxBatchSize, token);
                return new CachedPage(result.Items.Cast<object>().ToList(), result.HasNextPage);
            },
            cancellationToken);

        return new AniListPageResult<AniListMedia>(
            cached.Items.Cast<AniListMedia>().ToList(),
            page,
            cached.HasNextPage);
    }

    public async Task<AiringScheduleLoad> GetAiringSchedulesAsync(
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        IProgress<AiringScheduleLoad>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startUnix = windowStartInclusive.ToUnixTimeSeconds();
        var endUnix = windowEndExclusive.ToUnixTimeSeconds();
        var ttl = windowEndExclusive < _timeProvider.GetUtcNow() ? HistoricalTtl : SuccessTtl;

        var schedules = new List<AniListAiringSchedule>();
        var page = 1;
        var truncated = false;
        string? degraded = null;

        while (true)
        {
            if (page > MaxAiringPages)
            {
                truncated = true;
                break;
            }

            CachedPage cached;

            try
            {
                cached = await ReadThroughAsync(
                    $"air|{startUnix}|{endUnix}|p{page}",
                    ttl,
                    async token =>
                    {
                        var result = await _aniListService.GetAiringSchedulesAsync(
                            windowStartInclusive, windowEndExclusive, page, AniListService.MaxBatchSize, token);

                        return new CachedPage(result.Items.Cast<object>().ToList(), result.HasNextPage);
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (page > 1)
            {
                // Losing page four of seven is not worth throwing away the three that arrived, so the
                // walk stops and says so. Only a first-page failure leaves nothing to show.
                degraded = exception is AniListUnavailableException
                    ? AniListUnavailableException.DefaultMessage
                    : exception.Message;

                break;
            }

            schedules.AddRange(cached.Items.Cast<AniListAiringSchedule>());

            var partial = BuildLoad(schedules, page, isComplete: false, truncated: false, degraded: null);
            progress?.Report(partial);

            if (!cached.HasNextPage)
            {
                break;
            }

            page++;
        }

        var load = BuildLoad(schedules, Math.Min(page, MaxAiringPages), isComplete: degraded is null, truncated, degraded);
        progress?.Report(load);

        return load;
    }

    private static AiringScheduleLoad BuildLoad(
        List<AniListAiringSchedule> schedules,
        int pagesLoaded,
        bool isComplete,
        bool truncated,
        string? degraded) =>
        new()
        {
            // Copied, because the caller holds onto progress reports while the walk keeps appending.
            Schedules = schedules.ToList(),
            PagesLoaded = pagesLoaded,
            IsComplete = isComplete,
            WasTruncated = truncated,
            CompleteThrough = schedules.Count == 0 ? null : schedules[^1].AiringAtUtc,
            DegradedMessage = degraded
        };

    /// <summary>
    /// Serves a cached page, joins an identical in-flight request, or issues one through the pacer.
    /// </summary>
    /// <remarks>
    /// The in-flight collapse is not an optimisation here. A double-click on "next week", or a
    /// re-render racing the initial load, would otherwise pay twice through a gate that only lets one
    /// request past every 2.1 seconds.
    /// </remarks>
    private async Task<CachedPage> ReadThroughAsync(
        string key,
        TimeSpan successTtl,
        Func<CancellationToken, Task<CachedPage>> fetch,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            if (cached.Failure is not null)
            {
                throw cached.Failure;
            }

            return cached.Page!;
        }

        if (_inFlight.TryGetValue(key, out var existing))
        {
            return await existing;
        }

        var task = FetchAndCacheAsync(key, successTtl, fetch, cancellationToken);
        _inFlight[key] = task;

        try
        {
            return await task;
        }
        finally
        {
            _inFlight.Remove(key);
        }
    }

    private async Task<CachedPage> FetchAndCacheAsync(
        string key,
        TimeSpan successTtl,
        Func<CancellationToken, Task<CachedPage>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _requestPacer.RunAsync(fetch, cancellationToken);
            _cache[key] = CacheEntry.ForPage(page, _timeProvider.GetUtcNow() + successTtl);
            return page;
        }
        catch (OperationCanceledException)
        {
            // Navigating away is routine and must not be remembered as a failure, or the next visit
            // is refused from cache for two minutes.
            throw;
        }
        catch (Exception exception)
        {
            _cache[key] = CacheEntry.ForFailure(exception, _timeProvider.GetUtcNow() + FailureTtl);
            throw;
        }
    }

    private bool IsHistoricalSeason(AniListBrowseRequest request) =>
        request.SeasonYear is { } year
        && request.Season is { } season
        && AnimeSeasonCalendar.IsHistorical(year, season, _timeProvider);

    /// <summary>
    /// One cached page. Items are held as object so a single cache can hold both media and airing
    /// schedules without a second dictionary; the two key namespaces never overlap.
    /// </summary>
    private sealed record CachedPage(IReadOnlyList<object> Items, bool HasNextPage);

    private sealed record CacheEntry(CachedPage? Page, Exception? Failure, DateTimeOffset ExpiresAt)
    {
        public static CacheEntry ForPage(CachedPage page, DateTimeOffset expiresAt) => new(page, null, expiresAt);

        public static CacheEntry ForFailure(Exception failure, DateTimeOffset expiresAt) => new(null, failure, expiresAt);
    }
}
