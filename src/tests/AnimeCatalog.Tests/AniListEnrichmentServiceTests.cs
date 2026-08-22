using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

public sealed class AniListEnrichmentServiceTests
{
    [Fact]
    public async Task GetAsync_CachesSuccessSoASecondCallIssuesNoRequest()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        var first = await service.GetAsync(20);
        var second = await service.GetAsync(20);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, aniList.CallCount);
    }

    [Fact]
    public async Task GetAsync_ConcurrentCallsForSameIdIssueOneRequest()
    {
        var gate = new TaskCompletionSource();
        var aniList = new RecordingAniListService(ids => ids.Select(Media), gate.Task);
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        var first = service.GetAsync(20);
        var second = service.GetAsync(20);

        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, Assert.NotNull);
        Assert.Equal(1, aniList.CallCount);
    }

    [Fact]
    public async Task GetManyAsync_ChunksIdsIntoBatchesOfFifty()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        var ids = Enumerable.Range(1, 120).ToList();
        var results = await service.GetManyAsync(ids);

        Assert.Equal(120, results.Count);
        Assert.Equal(3, aniList.CallCount);
        Assert.All(aniList.RequestedBatches, batch => Assert.True(batch.Count <= 50));
        Assert.Equal(120, aniList.RequestedBatches.Sum(batch => batch.Count));
    }

    [Fact]
    public async Task GetManyAsync_RequestsOnlyTheIdsNotAlreadyCached()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        await service.GetManyAsync([1, 2, 3]);
        await service.GetManyAsync([2, 3, 4]);

        Assert.Equal(2, aniList.CallCount);
        Assert.Equal([4], aniList.RequestedBatches[1]);
    }

    [Fact]
    public async Task GetManyAsync_DeduplicatesRepeatedIds()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        var results = await service.GetManyAsync([7, 7, 7]);

        Assert.Single(results);
        Assert.Equal([7], aniList.RequestedBatches[0]);
    }

    [Fact]
    public async Task GetManyAsync_MatchesResultsByIdNotByPosition()
    {
        // AniList returns batched media ordered by id, not in the order they were requested.
        var aniList = new RecordingAniListService(ids => ids.OrderByDescending(id => id).Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        var results = await service.GetManyAsync([16498, 21, 154587]);

        Assert.Equal(21, results[21].Id);
        Assert.Equal(16498, results[16498].Id);
        Assert.Equal(154587, results[154587].Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWithoutThrowingWhenAniListFails()
    {
        var aniList = new RecordingAniListService(_ => throw new HttpRequestException("AniList is down"));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        Assert.Null(await service.GetAsync(20));
    }

    [Fact]
    public async Task GetAsync_NegativeCachesFailureThenRetriesAfterTtl()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        var shouldFail = true;
        var aniList = new RecordingAniListService(ids => shouldFail
            ? throw new HttpRequestException("AniList is down")
            : ids.Select(Media));

        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(time, TimeSpan.Zero), time);

        Assert.Null(await service.GetAsync(20));
        Assert.Null(await service.GetAsync(20));
        Assert.Equal(1, aniList.CallCount);

        shouldFail = false;
        time.Advance(TimeSpan.FromMinutes(3));

        Assert.NotNull(await service.GetAsync(20));
        Assert.Equal(2, aniList.CallCount);
    }

    [Fact]
    public async Task GetAsync_CachesAnIdAniListDoesNotKnowAboutForTheFullWindow()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        // A successful call that simply omits the id: AniList genuinely has nothing here.
        var aniList = new RecordingAniListService(_ => []);
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(time, TimeSpan.Zero), time);

        Assert.Null(await service.GetAsync(999));
        time.Advance(TimeSpan.FromMinutes(3));
        Assert.Null(await service.GetAsync(999));

        Assert.Equal(1, aniList.CallCount);
    }

    [Fact]
    public async Task GetManyAsync_WithNoIdsIssuesNoRequest()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        Assert.Empty(await service.GetManyAsync([]));
        Assert.Equal(0, aniList.CallCount);
    }

    [Fact]
    public async Task GetAsync_CancellationDoesNotPoisonTheCache()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.Zero));

        Assert.Null(await service.GetAsync(20, cts.Token));

        // The cancelled attempt must not be remembered as a failure.
        Assert.NotNull(await service.GetAsync(20));
    }

    [Fact]
    public async Task GetManyAsync_SpacesOutRequestsSoABulkWalkStaysUnderTheRateLimit()
    {
        // A relation-graph walk issues dozens of batches back to back. Without spacing it trips
        // AniList's 30/min limit part-way through and whole batches become cached failures.
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.FromMilliseconds(120)));

        var started = DateTimeOffset.UtcNow;
        await service.GetManyAsync(Enumerable.Range(1, 120).ToList());
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(3, aniList.CallCount);
        // Three requests means two waits; the first goes straight out.
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(200), $"expected pacing, took {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task GetManyAsync_DoesNotPaceCacheHits()
    {
        var aniList = new RecordingAniListService(ids => ids.Select(Media));
        var service = new AniListEnrichmentService(aniList, new AniListRequestPacer(null, TimeSpan.FromSeconds(30)));

        await service.GetManyAsync([1]);

        // Already cached, so this must not sit behind the spacing delay.
        var started = DateTimeOffset.UtcNow;
        await service.GetManyAsync([1]);

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(1));
        Assert.Equal(1, aniList.CallCount);
    }

    private static AniListMedia Media(int id) => new() { Id = id };

    private sealed class RecordingAniListService : IAniListService
    {
        private readonly Func<IReadOnlyCollection<int>, IEnumerable<AniListMedia>> _handler;
        private readonly Task? _gate;

        public RecordingAniListService(
            Func<IReadOnlyCollection<int>, IEnumerable<AniListMedia>> handler,
            Task? gate = null)
        {
            _handler = handler;
            _gate = gate;
        }

        public int CallCount { get; private set; }

        public List<List<int>> RequestedBatches { get; } = [];

        public async Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            RequestedBatches.Add(ids.ToList());

            if (_gate is not null)
            {
                await _gate;
            }

            return _handler(ids).ToList();
        }

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The enrichment service always batches.");

        public Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AniListPageResult<AniListAiringSchedule>> GetAiringSchedulesAsync(
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This stub does not serve the calendar.");

        public Task<AniListPageResult<AniListMedia>> BrowseMediaAsync(
            AniListBrowseRequest request,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This stub does not serve the calendar.");
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
