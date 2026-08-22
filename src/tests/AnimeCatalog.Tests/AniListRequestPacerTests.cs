using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Tests;

/// <summary>
/// Covers the gate every AniList call shares.
/// </summary>
/// <remarks>
/// The property that matters is the one the extraction existed to buy: two unrelated callers - a
/// calendar page and the home page's enrichment - must queue behind the same spacing rather than
/// each keeping their own and colliding into a 429.
/// </remarks>
public sealed class AniListRequestPacerTests
{
    [Fact]
    public async Task TwoDifferentCallers_ShareOneSpacingBudget()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.FromMilliseconds(120));

        var started = DateTimeOffset.UtcNow;

        // Standing in for the two independent callers the shared gate exists for.
        await pacer.RunAsync(_ => Task.FromResult(1), CancellationToken.None);
        await pacer.RunAsync(_ => Task.FromResult(2), CancellationToken.None);
        await pacer.RunAsync(_ => Task.FromResult(3), CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - started;

        // Three requests means two waits; the first goes straight out.
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(200),
            $"expected the callers to share the spacing, took {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task TheFirstRequest_GoesOutWithoutWaiting()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.FromSeconds(30));

        var started = DateTimeOffset.UtcNow;
        await pacer.RunAsync(_ => Task.FromResult(1), CancellationToken.None);

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ZeroSpacing_IsANoOp()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.Zero);

        var started = DateTimeOffset.UtcNow;

        for (var index = 0; index < 20; index++)
        {
            await pacer.RunAsync(_ => Task.FromResult(index), CancellationToken.None);
        }

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ConcurrentCallers_AreSerialised()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.Zero);
        var running = 0;
        var maxObserved = 0;

        var calls = Enumerable.Range(0, 8).Select(_ => pacer.RunAsync(async _ =>
        {
            var current = Interlocked.Increment(ref running);
            maxObserved = Math.Max(maxObserved, current);
            await Task.Yield();
            Interlocked.Decrement(ref running);
            return current;
        }, CancellationToken.None));

        await Task.WhenAll(calls);

        Assert.Equal(1, maxObserved);
    }

    // Navigating away mid-load has to release the gate during the wait, not after the next request,
    // or three fast clicks on "next week" would leave loads queued behind a spacing delay each.
    [Fact]
    public async Task CancellingDuringTheWait_ReleasesTheGateForTheNextCaller()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource();

        await pacer.RunAsync(_ => Task.FromResult(1), CancellationToken.None);

        var cancelled = pacer.RunAsync(_ => Task.FromResult(2), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        // The gate is free again, so an uncancelled caller still gets through.
        Assert.Equal(3, await pacer.RunAsync(_ => Task.FromResult(3), CancellationToken.None));
    }

    [Fact]
    public async Task AFailingRequest_StillReleasesTheGate()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pacer.RunAsync<int>(_ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(7, await pacer.RunAsync(_ => Task.FromResult(7), CancellationToken.None));
    }

    [Fact]
    public async Task TheCancellationToken_ReachesTheRequest()
    {
        var pacer = new AniListRequestPacer(null, TimeSpan.Zero);
        using var cts = new CancellationTokenSource();

        var observed = await pacer.RunAsync(token => Task.FromResult(token), cts.Token);

        Assert.Equal(cts.Token, observed);
    }
}
