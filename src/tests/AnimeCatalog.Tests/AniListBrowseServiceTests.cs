using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class AniListBrowseServiceTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ItWalksEveryPageUntilAniListRunsOut()
    {
        var aniList = new StubAniListService { AiringPages = [Schedules(50), Schedules(50), Schedules(7)] };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(107, load.Schedules.Count);
        Assert.Equal(3, aniList.AiringCallCount);
        Assert.True(load.IsComplete);
        Assert.False(load.WasTruncated);
    }

    // The behaviour verified against the live API: page 6 of a five-page season came back empty with
    // hasNextPage still true. Without the empty-page backstop every browse runs to its page cap.
    [Fact]
    public async Task AnEmptyPage_EndsTheWalkEvenWhenAniListStillClaimsThereIsMore()
    {
        var aniList = new StubAniListService
        {
            AiringPages = [Schedules(50), Schedules(0)],
            AlwaysClaimAnotherPage = true
        };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(50, load.Schedules.Count);
        Assert.Equal(2, aniList.AiringCallCount);
        Assert.True(load.IsComplete);
    }

    [Fact]
    public async Task HittingThePageCap_IsReportedAsTruncatedRatherThanPassedOffAsComplete()
    {
        var aniList = new StubAniListService
        {
            AiringPages = Enumerable.Range(0, 20).Select(_ => Schedules(50)).ToList(),
            AlwaysClaimAnotherPage = true
        };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(AniListBrowseService.MaxAiringPages, aniList.AiringCallCount);
        Assert.True(load.WasTruncated);
        Assert.True(load.IsDegraded);
    }

    [Fact]
    public async Task ProgressIsReportedOncePerPage_SoDayColumnsFillAsResultsArrive()
    {
        var aniList = new StubAniListService { AiringPages = [Schedules(50), Schedules(50), Schedules(3)] };
        var service = Create(aniList);

        // A synchronous IProgress, deliberately not Progress<T>: that type marshals its callback
        // through the synchronization context, so the reports arrive after the await and a test built
        // on it either races or has to poll. The page uses Progress<T> because it needs the render to
        // happen on the renderer's context; the contract being asserted here is just "one report per
        // page, plus a final one", which is observable synchronously.
        var reports = new RecordingProgress();

        await service.GetAiringSchedulesAsync(WindowStart, WindowEnd, reports);

        // Three page reports plus the final one.
        Assert.Equal(4, reports.Reports.Count);
        Assert.Equal([50, 100, 103, 103], reports.Reports.Select(report => report.Schedules.Count).ToArray());

        // Only the last one claims completion.
        Assert.Equal([false, false, false, true], reports.Reports.Select(report => report.IsComplete).ToArray());
    }

    // A first-page failure leaves nothing to show, so it has to surface.
    [Fact]
    public async Task AFailureOnTheFirstPage_Throws()
    {
        var aniList = new StubAniListService { FailOnAiringPage = 1 };
        var service = Create(aniList);

        await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.GetAiringSchedulesAsync(WindowStart, WindowEnd));
    }

    // Losing page three is not worth throwing away the two that arrived.
    [Fact]
    public async Task AFailureLaterOn_KeepsWhatLoadedAndSaysSo()
    {
        var aniList = new StubAniListService
        {
            AiringPages = [Schedules(50), Schedules(50)],
            AlwaysClaimAnotherPage = true,
            FailOnAiringPage = 3
        };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(100, load.Schedules.Count);
        Assert.False(load.IsComplete);
        Assert.NotNull(load.DegradedMessage);
        Assert.True(load.IsDegraded);

        // The time sort means the missing part is the END of the week, so the boundary is nameable.
        Assert.NotNull(load.CompleteThrough);
    }

    [Fact]
    public async Task CompleteThrough_IsTheLastAiringTimeActuallyReceived()
    {
        var last = new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero);
        var aniList = new StubAniListService
        {
            AiringPages = [[Schedule(1, WindowStart), Schedule(2, last)]]
        };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(last, load.CompleteThrough);
    }

    [Fact]
    public async Task AnEmptyWindow_ReportsNoBoundaryRatherThanADefaultDate()
    {
        var aniList = new StubAniListService { AiringPages = [Schedules(0)] };
        var service = Create(aniList);

        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Empty(load.Schedules);
        Assert.Null(load.CompleteThrough);
        Assert.True(load.IsComplete);
    }

    [Fact]
    public async Task TheSameWindowTwice_CostsOneSetOfRequests()
    {
        var aniList = new StubAniListService { AiringPages = [Schedules(10)] };
        var service = Create(aniList);

        await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);
        var afterFirst = aniList.AiringCallCount;

        await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        Assert.Equal(afterFirst, aniList.AiringCallCount);
    }

    [Fact]
    public async Task TwoConcurrentReadsOfTheSamePage_CollapseIntoOneRequest()
    {
        var gate = new TaskCompletionSource();
        var aniList = new StubAniListService { AiringPages = [Schedules(5)], Gate = gate.Task };
        var service = Create(aniList);

        var first = service.GetAiringSchedulesAsync(WindowStart, WindowEnd);
        var second = service.GetAiringSchedulesAsync(WindowStart, WindowEnd);

        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, aniList.AiringCallCount);
    }

    [Fact]
    public async Task TheSameBrowseRequestTwice_CostsOneRequest()
    {
        var aniList = new StubAniListService { BrowsePages = [Media(50)] };
        var service = Create(aniList);

        var request = new AniListBrowseRequest { SeasonYear = 2011, Season = "SPRING" };

        await service.GetBrowsePageAsync(request, 1);
        await service.GetBrowsePageAsync(request, 1);

        Assert.Equal(1, aniList.BrowseCallCount);
    }

    [Fact]
    public async Task ADifferentSort_IsADifferentRequest()
    {
        var aniList = new StubAniListService { BrowsePages = [Media(50), Media(50)] };
        var service = Create(aniList);

        await service.GetBrowsePageAsync(new AniListBrowseRequest { SeasonYear = 2011, Sort = "POPULARITY_DESC" }, 1);
        await service.GetBrowsePageAsync(new AniListBrowseRequest { SeasonYear = 2011, Sort = "SCORE_DESC" }, 1);

        Assert.Equal(2, aniList.BrowseCallCount);
    }

    // The filters a visitor would call identical must not cost two paced requests.
    [Fact]
    public async Task TheSameFiltersInADifferentOrder_ShareOneCacheEntry()
    {
        var aniList = new StubAniListService { BrowsePages = [Media(50)] };
        var service = Create(aniList);

        await service.GetBrowsePageAsync(
            new AniListBrowseRequest { SeasonYear = 2011, Formats = ["TV", "MOVIE"], Genres = ["Action", "Drama"] }, 1);

        await service.GetBrowsePageAsync(
            new AniListBrowseRequest { SeasonYear = 2011, Formats = ["MOVIE", "TV"], Genres = ["Drama", "Action"] }, 1);

        Assert.Equal(1, aniList.BrowseCallCount);
    }

    [Fact]
    public async Task ADifferentPage_IsADifferentCacheEntry()
    {
        var aniList = new StubAniListService { BrowsePages = [Media(50), Media(50)] };
        var service = Create(aniList);

        var request = new AniListBrowseRequest { SeasonYear = 2011 };

        await service.GetBrowsePageAsync(request, 1);
        await service.GetBrowsePageAsync(request, 2);

        Assert.Equal(2, aniList.BrowseCallCount);
    }

    [Fact]
    public async Task AFailedBrowse_IsHeldBrieflyRatherThanRetriedOnEveryRender()
    {
        var aniList = new StubAniListService { FailOnBrowsePage = 1 };
        var service = Create(aniList);

        var request = new AniListBrowseRequest { SeasonYear = 2011 };

        await Assert.ThrowsAsync<AniListUnavailableException>(() => service.GetBrowsePageAsync(request, 1));
        await Assert.ThrowsAsync<AniListUnavailableException>(() => service.GetBrowsePageAsync(request, 1));

        // The second attempt came from the cached failure, not from the network.
        Assert.Equal(1, aniList.BrowseCallCount);
    }

    [Fact]
    public async Task Cancellation_IsNotRememberedAsAFailure()
    {
        var aniList = new StubAniListService { AiringPages = [Schedules(5)] };
        var service = Create(aniList);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAiringSchedulesAsync(WindowStart, WindowEnd, null, cts.Token));

        // Nothing was cached as a refusal, so a fresh read still works.
        var load = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd);
        Assert.Equal(5, load.Schedules.Count);
    }

    private static AniListBrowseService Create(IAniListService aniList) =>
        new(aniList,
            new AniListRequestPacer(null, TimeSpan.Zero),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero)));

    private static List<AniListAiringSchedule> Schedules(int count) =>
        Enumerable.Range(1, count).Select(index => Schedule(index, WindowStart.AddHours(index))).ToList();

    private static AniListAiringSchedule Schedule(int id, DateTimeOffset airsAt) => new()
    {
        Id = id,
        MediaId = id,
        Episode = 1,
        AiringAt = airsAt.ToUnixTimeSeconds(),
        Media = new AniListMedia { Id = id }
    };

    private static List<AniListMedia> Media(int count) =>
        Enumerable.Range(1, count).Select(index => new AniListMedia { Id = index }).ToList();

    /// <summary>Records progress reports on the calling thread, so assertions need no polling.</summary>
    private sealed class RecordingProgress : IProgress<AiringScheduleLoad>
    {
        public List<AiringScheduleLoad> Reports { get; } = [];

        public void Report(AiringScheduleLoad value) => Reports.Add(value);
    }

    private sealed class StubAniListService : IAniListService
    {
        public List<List<AniListAiringSchedule>> AiringPages { get; init; } = [];

        public List<List<AniListMedia>> BrowsePages { get; init; } = [];

        /// <summary>Forces hasNextPage true regardless, to exercise the empty-page backstop and the cap.</summary>
        public bool AlwaysClaimAnotherPage { get; init; }

        public int? FailOnAiringPage { get; init; }

        public int? FailOnBrowsePage { get; init; }

        public Task? Gate { get; init; }

        public int AiringCallCount { get; private set; }

        public int BrowseCallCount { get; private set; }

        public async Task<AniListPageResult<AniListAiringSchedule>> GetAiringSchedulesAsync(
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiringCallCount++;

            if (Gate is not null)
            {
                await Gate;
            }

            if (FailOnAiringPage == page)
            {
                throw new AniListUnavailableException(403, "disabled");
            }

            var items = page <= AiringPages.Count ? AiringPages[page - 1] : [];
            var hasNext = AlwaysClaimAnotherPage || page < AiringPages.Count;

            return new AniListPageResult<AniListAiringSchedule>(items, page, items.Count > 0 && hasNext);
        }

        public Task<AniListPageResult<AniListMedia>> BrowseMediaAsync(
            AniListBrowseRequest request,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BrowseCallCount++;

            if (FailOnBrowsePage == page)
            {
                throw new AniListUnavailableException(403, "disabled");
            }

            var items = page <= BrowsePages.Count ? BrowsePages[page - 1] : [];
            var hasNext = AlwaysClaimAnotherPage || page < BrowsePages.Count;

            return Task.FromResult(new AniListPageResult<AniListMedia>(items, page, items.Count > 0 && hasNext));
        }

        public Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
