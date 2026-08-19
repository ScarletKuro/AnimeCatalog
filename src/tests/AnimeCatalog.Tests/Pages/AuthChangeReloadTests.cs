using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Options;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

// The reported bug: signing out of a private catalog left the already-rendered content on screen
// until the visitor navigated or hard-refreshed. Every case here asserts the swap happens in place -
// the NavigationManager assertion is what separates the fix from a page that only recovered because
// something navigated.
public sealed class AuthChangeReloadTests
{
    [Fact]
    public async Task Catalog_SigningOut_ReplacesTheFiltersWithThePrivateCard_WithoutNavigating()
    {
        await using var context = CreateContext("catalog", out var access, out var auth);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = context.Render<Catalog>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".filters-card")));

        var uriBeforeSignOut = navigation.Uri;
        access.CanRead = false;
        auth.SignOut();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.Empty(cut.FindAll(".filters-card"));
        });

        Assert.Equal(uriBeforeSignOut, navigation.Uri);
    }

    [Fact]
    public async Task Home_SigningOut_ReplacesTheSummaryWithThePrivateCard()
    {
        await using var context = CreateContext(string.Empty, out var access, out var auth);

        var cut = context.Render<Home>();
        cut.WaitForAssertion(() => Assert.DoesNotContain(CatalogAccess.PrivateMessage, cut.Markup));

        access.CanRead = false;
        auth.SignOut();

        cut.WaitForAssertion(() => Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup));
    }

    [Fact]
    public async Task Franchise_SigningOut_ReplacesTheDetailsWithThePrivateCard()
    {
        await using var context = CreateContext("franchise/gundam", out var access, out var auth);

        var cut = context.Render<Franchise>(parameters => parameters.Add(p => p.Slug, "gundam"));
        cut.WaitForAssertion(() => Assert.DoesNotContain(CatalogAccess.PrivateMessage, cut.Markup));

        access.CanRead = false;
        auth.SignOut();

        cut.WaitForAssertion(() => Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup));
    }

    [Fact]
    public async Task AnimeDetails_SigningOut_ReplacesTheEntryWithThePrivateCard()
    {
        await using var context = CreateContext("anime/174", out var access, out var auth);

        var cut = context.Render<AnimeDetails>(parameters => parameters.Add(p => p.Id, 174L));
        cut.WaitForAssertion(() => Assert.DoesNotContain(CatalogAccess.PrivateMessage, cut.Markup));

        access.CanRead = false;
        auth.SignOut();

        cut.WaitForAssertion(() => Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup));
    }

    [Fact]
    public async Task Catalog_SigningOut_HidesTheFiltersWhileTheSecondAccessCheckIsStillInFlight()
    {
        // Catalog gates the filters card on _hasLoaded, not _isLoading, so without the reset in
        // ReloadForAuthChangeAsync a private catalog would keep a usable-looking search bar on screen
        // for the whole length of the re-check before refusing access.
        await using var context = CreateContext("catalog", out var access, out var auth);

        var cut = context.Render<Catalog>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".filters-card")));

        access.CanRead = false;
        access.HoldTheNextCheck();
        auth.SignOut();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".filters-card"));
            Assert.Contains("Loading catalog...", cut.Markup);
        });

        access.ReleaseTheHeldCheck();
        cut.WaitForAssertion(() => Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup));
    }

    [Fact]
    public async Task Catalog_TokenRefresh_LeavesTheTypedFiltersAlone()
    {
        // AuthService raises StateChanged on every silent refresh. Reloading there would reset the
        // search box under whoever was typing in it, for a change that cannot affect what the
        // catalog RPCs return.
        await using var context = CreateContext("catalog", out var access, out var auth);

        var cut = context.Render<Catalog>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".filters-card")));

        cut.Find(".text-input").Input("cowboy bebop");
        cut.WaitForAssertion(() => Assert.Equal("cowboy bebop", cut.Find(".text-input").GetAttribute("value")));

        var checksBeforeRefresh = access.CheckCount;
        auth.RaiseWithoutIdentityChange();

        Assert.Equal(checksBeforeRefresh, access.CheckCount);
        Assert.Equal("cowboy bebop", cut.Find(".text-input").GetAttribute("value"));
    }

    private static BunitContext CreateContext(
        string relativeUri,
        out SwitchableCatalogAccessService access,
        out StubAuthStateNotifier auth)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        access = new SwitchableCatalogAccessService();
        auth = new StubAuthStateNotifier("owner", isAdmin: true);

        context.Services.AddSingleton<IAuthStateNotifier>(auth);
        context.Services.AddSingleton<ICatalogAccessService>(access);
        context.Services.AddSingleton<ISupabaseRestService>(new EmptySupabaseRestService());
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton<IAniListService>(new NoOpAniListService());
        context.Services.AddSingleton<IAdminAuthorizationService>(new AlwaysAdminAuthorizationService());
        context.Services.AddSingleton(new FranchiseService());
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>()));
        context.Services.AddSingleton(sp => new AuthService(
            new HttpClient(),
            new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
            sp.GetRequiredService<NavigationManager>(),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "sb_publishable_123"
            }),
            new AppAuthenticationStateProvider()));
        context.Services.AddSingleton(sp => new AdminCatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<IAniListService>(),
            sp.GetRequiredService<IAdminAuthorizationService>(),
            sp.GetRequiredService<CatalogService>()));

        context.Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);

        return context;
    }

    // Stands in for the server-side visibility toggle: flipping CanRead is what signing out does to
    // the answer can_read_catalog gives the very same request.
    private sealed class SwitchableCatalogAccessService : ICatalogAccessService
    {
        private TaskCompletionSource<bool>? _gate;

        public bool CanRead { get; set; } = true;

        public int CheckCount { get; private set; }

        public void HoldTheNextCheck() => _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseTheHeldCheck() => _gate?.TrySetResult(CanRead);

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return _gate?.Task ?? Task.FromResult(CanRead);
        }

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CanRead);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class EmptySupabaseRestService : ISupabaseRestService
    {
        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => Task.FromResult(new List<T>());

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoOpAniListEnrichmentService : IAniListEnrichmentService
    {
        public Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(IReadOnlyCollection<int> aniListIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, AniListMedia>>(new Dictionary<int, AniListMedia>());
    }

    private sealed class NoOpAniListService : IAniListService
    {
        public Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AniListMedia>>([]);

        public Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AniListMedia>>([]);
    }

    private sealed class AlwaysAdminAuthorizationService : IAdminAuthorizationService
    {
        public Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
