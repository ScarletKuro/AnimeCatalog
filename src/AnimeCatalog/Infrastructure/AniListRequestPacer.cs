namespace AnimeCatalog.Infrastructure;

/// <summary>
/// The one gate every AniList network call in the app passes through.
/// </summary>
/// <remarks>
/// <para>
/// AniList's per-IP limit is currently far below its documented 90 requests a minute - roughly 30 -
/// and every call is made from the visitor's own browser, so the whole app shares one budget.
/// </para>
/// <para>
/// This used to live inside <c>AniListEnrichmentService</c>, which was correct while enrichment was
/// the only caller. It is not any more: a calendar week is five to seven sequential pages, and a
/// second private gate would let a calendar load and a home-page enrichment burst collide into a
/// 429 - which turns whole batches into cached failures and blank cards, with nothing shown to the
/// visitor to explain it. Spacing them is cheaper than recovering from a 429.
/// </para>
/// <para>
/// Registered as a singleton rather than scoped. In WebAssembly the two are the same thing, but
/// singleton states the intent - there must be exactly one of these - and it stops a future
/// AddScoped from quietly producing two gates and reintroducing the bug this type exists to
/// prevent.
/// </para>
/// </remarks>
public sealed class AniListRequestPacer
{
    /// <summary>AniList allows 30 requests a minute, so one every two seconds stays just inside it.</summary>
    public static readonly TimeSpan DefaultRequestSpacing = TimeSpan.FromMilliseconds(2100);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minRequestSpacing;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public AniListRequestPacer(TimeProvider? timeProvider = null, TimeSpan? minRequestSpacing = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _minRequestSpacing = minRequestSpacing ?? DefaultRequestSpacing;
    }

    /// <summary>
    /// Runs one AniList call, holding it until the spacing window opens.
    /// </summary>
    /// <remarks>
    /// Only calls that actually reach the network belong here - a cache hit queueing behind the gate
    /// would pay for a request it never makes. Cancellation takes effect during the wait as well as
    /// before it, so a page that navigates away mid-load releases the gate at once rather than after
    /// its next request.
    /// </remarks>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await PaceAsync(cancellationToken);
            return await request(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Holds outgoing requests to AniList's documented ceiling.
    /// </summary>
    /// <remarks>
    /// A bulk walk of the relation graph, or a seven-page calendar week, issues request after
    /// request and would otherwise trip the 30/min limit part-way through, turning whole batches into
    /// cached failures and silently dropping results.
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
}
