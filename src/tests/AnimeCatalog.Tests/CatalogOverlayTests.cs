using System.Net;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// Covers the one method on CatalogService that must never throw for a refusal.
/// </summary>
/// <remarks>
/// The calendar's AniList half is public and has to render for a visitor who cannot read the
/// catalog at all, so every refusal has to arrive as a state plus an empty map. A throw here would
/// take down a page whose main content does not depend on Supabase in the first place.
/// </remarks>
public sealed class CatalogOverlayTests
{
    [Fact]
    public async Task ItMapsAniListIdsOntoTheLocalEntryAndItsProgress()
    {
        var service = Create(
            animeRows:
            [
                new AnimeEntryRow { Id = 7, AniListId = 21, TitleRomaji = "One Piece", Episodes = 1000, DisplayOrder = 0 }
            ],
            catalogRows:
            [
                new CatalogEntryRow { Id = 1, AnimeEntryId = 7, Status = "watching", EpisodesWatched = 500, Score = 9m }
            ]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.True(overlay.IsDecorating);
        var item = Assert.Single(overlay.ByAniListId).Value;

        Assert.Equal(7, item.AnimeEntryId);
        Assert.Equal(21, item.AniListId);
        Assert.Equal(CatalogStatus.Watching, item.Status);
        Assert.Equal(500, item.EpisodesWatched);
        Assert.Equal(9m, item.Score);
        Assert.Equal(50, item.ProgressPercent);
    }

    [Fact]
    public async Task Find_ReturnsNullForATitleThatIsNotInTheCatalog()
    {
        var service = Create(
            animeRows: [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", DisplayOrder = 0 }],
            catalogRows: [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "completed", EpisodesWatched = 12 }]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.NotNull(overlay.Find(21));
        Assert.Null(overlay.Find(999));
    }

    // Entries added by hand carry no AniList counterpart, so they can never be matched against an
    // AniList id and must not occupy the 0 key.
    [Fact]
    public async Task EntriesWithNoAniListCounterpart_AreLeftOut()
    {
        var service = Create(
            animeRows:
            [
                new AnimeEntryRow { Id = 1, AniListId = 0, TitleRomaji = "Hand added", DisplayOrder = 0 },
                new AnimeEntryRow { Id = 2, AniListId = 21, TitleRomaji = "Real", DisplayOrder = 1 }
            ],
            catalogRows:
            [
                new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "planned", EpisodesWatched = 0 },
                new CatalogEntryRow { Id = 2, AnimeEntryId = 2, Status = "planned", EpisodesWatched = 0 }
            ]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal([21], overlay.ByAniListId.Keys.Order().ToArray());
    }

    // anime_entries has no uniqueness constraint on anilist_id, and a ToDictionary would throw over
    // a decoration - taking the whole calendar down with it.
    [Fact]
    public async Task ADuplicateAniListId_DoesNotThrow()
    {
        var service = Create(
            animeRows:
            [
                new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "First", DisplayOrder = 0 },
                new AnimeEntryRow { Id = 2, AniListId = 21, TitleRomaji = "Duplicate", DisplayOrder = 1 }
            ],
            catalogRows: [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "watching", EpisodesWatched = 3 }]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Single(overlay.ByAniListId);
        Assert.Equal(1, overlay.Find(21)!.AnimeEntryId);
    }

    [Fact]
    public async Task AnEntryWithNoCatalogRow_StillProjectsWithoutThrowing()
    {
        var service = Create(
            animeRows: [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", Episodes = 12, DisplayOrder = 0 }],
            catalogRows: []);

        var overlay = await service.GetCatalogOverlayAsync();

        var item = overlay.Find(21);
        Assert.NotNull(item);
        Assert.Equal(0, item.EpisodesWatched);
        Assert.Null(item.Score);
    }

    // An unknown episode count must not render as a 0% bar claiming no progress.
    [Fact]
    public async Task AnUnknownEpisodeCount_YieldsNoProgressPercentAtAll()
    {
        var service = Create(
            animeRows: [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", Episodes = null, DisplayOrder = 0 }],
            catalogRows: [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "watching", EpisodesWatched = 4 }]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Null(overlay.Find(21)!.ProgressPercent);
    }

    [Fact]
    public async Task WatchingMoreEpisodesThanExist_IsClampedRatherThanOverflowing()
    {
        var service = Create(
            animeRows: [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", Episodes = 12, DisplayOrder = 0 }],
            catalogRows: [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "completed", EpisodesWatched = 25 }]);

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal(100, overlay.Find(21)!.ProgressPercent);
    }

    [Fact]
    public async Task AnUnconfiguredSupabase_ReportsItWithoutReadingAnything()
    {
        var rest = new CountingSupabaseRestService([], [], isConfigured: false);
        var service = new CatalogService(rest, new FranchiseService(), new FakeAccess());

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal(CatalogAccessState.NotConfigured, overlay.State);
        Assert.False(overlay.IsDecorating);
        Assert.Empty(overlay.ByAniListId);

        // Nothing to ask, so nothing should have been asked.
        Assert.Equal(0, rest.SelectCallCount);

        // Nothing is broken either, so there is nothing to explain to the visitor.
        Assert.False(overlay.ShouldExplainAbsence);
    }

    [Fact]
    public async Task APrivateCatalog_ReportsItAsPrivateRatherThanThrowing()
    {
        var service = new CatalogService(
            new CountingSupabaseRestService([], []),
            new FranchiseService(),
            new FakeAccess(canReadCatalog: false));

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal(CatalogAccessState.Private, overlay.State);
        Assert.False(overlay.IsDecorating);
        Assert.Empty(overlay.ByAniListId);
        Assert.True(overlay.ShouldExplainAbsence);
    }

    [Fact]
    public async Task ATransportFailure_ReportsAnErrorRatherThanThrowing()
    {
        var service = new CatalogService(
            new ThrowingSupabaseRestService(new HttpRequestException("boom")),
            new FranchiseService(),
            new FakeAccess());

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal(CatalogAccessState.Error, overlay.State);
        Assert.Empty(overlay.ByAniListId);
    }

    [Fact]
    public async Task AForbiddenReadIsTreatedAsPrivate_NotAsAnError()
    {
        var service = new CatalogService(
            new ThrowingSupabaseRestService(
                new PostgrestException(new PostgrestError { Message = "forbidden", Code = "42501" }, (int)HttpStatusCode.Forbidden)),
            new FranchiseService(),
            new FakeAccess());

        var overlay = await service.GetCatalogOverlayAsync();

        Assert.Equal(CatalogAccessState.Private, overlay.State);
    }

    [Fact]
    public async Task ASecondReadWithinTheTtl_DoesNotHitSupabaseAgain()
    {
        var rest = new CountingSupabaseRestService(
            [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", DisplayOrder = 0 }],
            [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "watching", EpisodesWatched = 1 }]);

        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
        var service = new CatalogService(rest, new FranchiseService(), new FakeAccess(), time);

        await service.GetCatalogOverlayAsync();
        var afterFirst = rest.SelectCallCount;

        await service.GetCatalogOverlayAsync();

        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    [Fact]
    public async Task OnceTheTtlExpires_ItReadsAgain()
    {
        var rest = new CountingSupabaseRestService(
            [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", DisplayOrder = 0 }],
            []);

        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
        var service = new CatalogService(rest, new FranchiseService(), new FakeAccess(), time);

        await service.GetCatalogOverlayAsync();
        var afterFirst = rest.SelectCallCount;

        time.Advance(TimeSpan.FromMinutes(6));
        await service.GetCatalogOverlayAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    [Fact]
    public async Task Invalidating_ForcesTheNextReadThrough()
    {
        var rest = new CountingSupabaseRestService(
            [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", DisplayOrder = 0 }],
            []);

        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
        var service = new CatalogService(rest, new FranchiseService(), new FakeAccess(), time);

        await service.GetCatalogOverlayAsync();
        var afterFirst = rest.SelectCallCount;

        service.InvalidateCatalogOverlay();
        await service.GetCatalogOverlayAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    // Navigating away is not a refusal. Caching it as one would leave the next visit staring at an
    // empty overlay for the whole failure window.
    [Fact]
    public async Task Cancellation_PropagatesAndIsNotCachedAsARefusal()
    {
        var rest = new ThrowingSupabaseRestService(new OperationCanceledException());
        var service = new CatalogService(rest, new FranchiseService(), new FakeAccess());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetCatalogOverlayAsync());

        // Nothing was cached, so a working read still gets through.
        var working = new CatalogService(
            new CountingSupabaseRestService(
                [new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "A", DisplayOrder = 0 }],
                []),
            new FranchiseService(),
            new FakeAccess());

        Assert.True((await working.GetCatalogOverlayAsync()).IsDecorating);
    }

    private static CatalogService Create(
        IReadOnlyList<AnimeEntryRow> animeRows,
        IReadOnlyList<CatalogEntryRow> catalogRows) =>
        new(new CountingSupabaseRestService(animeRows, catalogRows), new FranchiseService(), new FakeAccess());

    private sealed class FakeAccess : ICatalogAccessService
    {
        private readonly bool _canReadCatalog;

        public FakeAccess(bool canReadCatalog = true) => _canReadCatalog = canReadCatalog;

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_canReadCatalog);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CountingSupabaseRestService : ISupabaseRestService
    {
        private readonly IReadOnlyList<AnimeEntryRow> _animeRows;
        private readonly IReadOnlyList<CatalogEntryRow> _catalogRows;

        public CountingSupabaseRestService(
            IReadOnlyList<AnimeEntryRow> animeRows,
            IReadOnlyList<CatalogEntryRow> catalogRows,
            bool isConfigured = true)
        {
            _animeRows = animeRows;
            _catalogRows = catalogRows;
            IsConfigured = isConfigured;
        }

        public bool IsConfigured { get; }

        public int SelectCallCount { get; private set; }

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
        {
            SelectCallCount++;

            IEnumerable<T> rows = table switch
            {
                "anime_entries" => _animeRows.Cast<T>(),
                "catalog_entries" => _catalogRows.Cast<T>(),
                "anime_relations" => Array.Empty<AnimeRelationRow>().Cast<T>(),
                "franchises" => Array.Empty<FranchiseRow>().Cast<T>(),
                _ => throw new NotSupportedException(table)
            };

            return Task.FromResult(rows.ToList());
        }

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingSupabaseRestService : ISupabaseRestService
    {
        private readonly Exception _exception;

        public ThrowingSupabaseRestService(Exception exception) => _exception = exception;

        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => Task.FromException<List<T>>(_exception);

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
