using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Services;
using Microsoft.AspNetCore.Components;

namespace AnimeCatalog.Tests;

/// <summary>
/// Covers the four-table snapshot cache on <see cref="CatalogService"/>.
/// </summary>
/// <remarks>
/// Filtering, sorting and grouping all happen client-side, so a search term never reached Supabase
/// and every keystroke in the catalog's search box re-read four identical tables. The cache is what
/// stops that. Most of what is covered here is what must not be shared along with the rows: the
/// access check, the page that asked, and the identity it was read for.
/// </remarks>
public sealed class CatalogSnapshotCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ASecondReadInsideTheWindowDoesNotTouchTheTables()
    {
        var (service, rest, _, _) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;
        await service.GetSnapshotAsync();

        Assert.Equal(4, afterFirst);
        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    // The whole point: two different filter sets are two calls, and only the first costs anything.
    [Fact]
    public async Task DifferentFiltersShareTheOneRead()
    {
        var (service, rest, _, _) = Create();

        await service.GetCatalogAsync(new() { Query = "one" });
        var afterFirst = rest.SelectCallCount;
        await service.GetCatalogAsync(new() { Query = "two" });

        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    [Fact]
    public async Task TheRowsAreReReadOnceTheWindowHasPassed()
    {
        var (service, rest, time, _) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        time.Advance(TimeSpan.FromSeconds(61));
        await service.GetSnapshotAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    // The access check is not part of what is cached. A visitor whose access was revoked has to be
    // refused on the very next read, even while the rows they could once see sit in memory.
    [Fact]
    public async Task TheAccessCheckRunsEvenWhenTheRowsAreCached()
    {
        var (service, rest, _, access) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;
        access.CanRead = false;

        await Assert.ThrowsAsync<CatalogAccessDeniedException>(() => service.GetSnapshotAsync());

        Assert.Equal(2, access.CheckCount);
        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    // And the refusal takes the rows with it, rather than leaving them to expire on their own.
    [Fact]
    public async Task ARefusalDropsWhatWasAlreadyRead()
    {
        var (service, rest, _, access) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        access.CanRead = false;
        await Assert.ThrowsAsync<CatalogAccessDeniedException>(() => service.GetSnapshotAsync());

        access.CanRead = true;
        await service.GetSnapshotAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    // Row-level security decides what those four reads return, so the admin's snapshot must not be
    // handed to the anonymous visitor the same browser turns into a moment later. Keyed on identity
    // inside the service rather than invalidated by each page, because six pages reload on a
    // sign-out and any one of them could forget.
    [Fact]
    public async Task SigningOutDoesNotServeThePreviousIdentityTheirRows()
    {
        var auth = new StubAuthStateNotifier("owner", isAdmin: true);
        var (service, rest, _, _) = Create(auth);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        auth.SignOut();
        await service.GetSnapshotAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    [Fact]
    public async Task SigningInDoesNotServeTheAnonymousRows()
    {
        var auth = new StubAuthStateNotifier();
        var (service, rest, _, _) = Create(auth);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        auth.SignInAs("owner", isAdmin: true);
        await service.GetSnapshotAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    // A token refresh raises the same event with the identity untouched. Dropping the cache on one
    // would undo the point of holding it, and AuthStateWatcher already treats it as a non-event.
    [Fact]
    public async Task ATokenRefreshKeepsTheCachedRows()
    {
        var auth = new StubAuthStateNotifier("owner", isAdmin: true);
        var (service, rest, _, _) = Create(auth);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        auth.RaiseWithoutIdentityChange();
        await service.GetSnapshotAsync();

        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    // The case that decides the design. Add an entry on one device, then on the other walk off the
    // catalog and back: without keying on the path this answers from cache, and a catalog missing the
    // entry you just saved does not read as stale, it reads as a failed save.
    [Fact]
    public async Task WalkingOffThePageAndBackReadsAgain()
    {
        var navigation = new MovableNavigationManager("catalog");
        var (service, rest, _, _) = Create(navigation: navigation);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        navigation.Go("calendar");
        await service.GetSnapshotAsync();
        navigation.Go("catalog");
        await service.GetSnapshotAsync();

        Assert.Equal(afterFirst * 3, rest.SelectCallCount);
    }

    // And the saving the cache exists for. A search term, a sort and a page number are all query
    // string, so none of them leaves the path and none of them costs a read.
    [Fact]
    public async Task ChangingOnlyTheQueryStringKeepsTheCachedRows()
    {
        var navigation = new MovableNavigationManager("catalog");
        var (service, rest, _, _) = Create(navigation: navigation);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        navigation.Go("catalog?q=one");
        await service.GetSnapshotAsync();
        navigation.Go("catalog?q=one&sort=Year&page=3");
        await service.GetSnapshotAsync();

        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    [Fact]
    public async Task AFragmentIsNotAPathEither()
    {
        var navigation = new MovableNavigationManager("catalog");
        var (service, rest, _, _) = Create(navigation: navigation);

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        navigation.Go("catalog#entries");
        await service.GetSnapshotAsync();

        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    [Fact]
    public async Task InvalidatingForcesTheNextReadThrough()
    {
        var (service, rest, _, _) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;

        service.InvalidateCachedReads();
        await service.GetSnapshotAsync();

        Assert.True(rest.SelectCallCount > afterFirst);
    }

    // The overlay is a projection of the snapshot, so it has to come off the same cached rows rather
    // than re-reading them - which is also why there is one invalidation method and not two.
    [Fact]
    public async Task TheOverlayIsBuiltFromTheCachedRows()
    {
        var (service, rest, _, _) = Create();

        await service.GetSnapshotAsync();
        var afterFirst = rest.SelectCallCount;
        var overlay = await service.GetCatalogOverlayAsync();

        Assert.True(overlay.IsDecorating);
        Assert.Equal(afterFirst, rest.SelectCallCount);
    }

    private static (CatalogService Service, CountingRestService Supabase, FixedTimeProvider Time, TogglingAccess Access) Create(
        StubAuthStateNotifier? auth = null,
        MovableNavigationManager? navigation = null)
    {
        var rest = new CountingRestService();
        var time = new FixedTimeProvider(Now);
        var access = new TogglingAccess();
        return (new CatalogService(rest, new FranchiseService(), access, time, auth, navigation), rest, time, access);
    }

    /// <summary>A NavigationManager a test can walk around, with nothing else attached to it.</summary>
    private sealed class MovableNavigationManager : NavigationManager
    {
        public MovableNavigationManager(string relativePath = "catalog")
        {
            Initialize("https://localhost:7227/", $"https://localhost:7227/{relativePath}");
        }

        public void Go(string relativePath) => Uri = $"https://localhost:7227/{relativePath}";

        // Nothing here navigates through the router; the tests only need Uri to move.
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class TogglingAccess : ICatalogAccessService
    {
        public bool CanRead { get; set; } = true;

        public int CheckCount { get; private set; }

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult(CanRead);
        }

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>Two entries, and a count of every table read that reached it.</summary>
    private sealed class CountingRestService : ISupabaseRestService
    {
        public int SelectCallCount { get; private set; }

        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
        {
            SelectCallCount++;

            IEnumerable<T> rows = table switch
            {
                "anime_entries" =>
                [
                    (T)(object)new AnimeEntryRow { Id = 1, AniListId = 21, TitleRomaji = "One", DisplayOrder = 0 },
                    (T)(object)new AnimeEntryRow { Id = 2, AniListId = 22, TitleRomaji = "Two", DisplayOrder = 0 }
                ],
                "catalog_entries" =>
                [
                    (T)(object)new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "watching" },
                    (T)(object)new CatalogEntryRow { Id = 2, AnimeEntryId = 2, Status = "completed" }
                ],
                "anime_relations" => [],
                "franchises" => [],
                _ => throw new NotSupportedException(table)
            };

            return Task.FromResult(rows.ToList());
        }

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
