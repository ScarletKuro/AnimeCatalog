using System.Net;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Options;
using AnimeCatalog.Pages;
using AnimeCatalog.Pages.Admin;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

// The admin half of the sign-out bug: these pages gate on AuthService.IsAdmin read at render time,
// so before the AuthStateWatcher nothing re-rendered them when the session went away. Unlike
// AuthChangeReloadTests these drive a real AuthService, because IsAdmin is what the markup reads.
public sealed class AdminAuthChangeTests
{
    private const string SessionStorageKey = "animeCatalog.auth.session";

    [Fact]
    public async Task Dashboard_SigningOut_ReplacesThePanelsWithTheAdminGate()
    {
        await using var context = CreateContext(out _, holdChecks: false);
        var authService = context.Services.GetRequiredService<AuthService>();
        await authService.InitializeAsync();

        var cut = context.Render<Dashboard>();
        cut.WaitForAssertion(() => Assert.Contains("Catalog visibility", cut.Markup));

        await authService.LogoutAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Authentication required", cut.Markup);
            Assert.DoesNotContain("Catalog visibility", cut.Markup);
            Assert.DoesNotContain("Backup and restore", cut.Markup);
        });
    }

    [Fact]
    public async Task WatchNext_SigningOut_CancelsTheScanAndShowsTheAdminGate()
    {
        // The gap scan is dozens of AniList calls made on the admin's behalf, so losing admin has to
        // stop it rather than just hide the results.
        // The access check is parked so the scan is genuinely in flight when the session goes away.
        await using var context = CreateContext(out var access, holdChecks: true);
        var authService = context.Services.GetRequiredService<AuthService>();
        await authService.InitializeAsync();

        var cut = context.Render<WatchNext>();
        access.WaitUntilACheckIsInFlight();

        await authService.LogoutAsync();

        cut.WaitForAssertion(() => Assert.Contains("Authentication required", cut.Markup));
        Assert.True(access.HeldCheckWasCancelled);
    }

    private static BunitContext CreateContext(out ObservableCatalogAccessService access, bool holdChecks)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var module = context.JSInterop.SetupModule("./js/auth.js");
        module.Setup<string?>("getItem", SessionStorageKey).SetResult(SerializeUnexpiredSession());

        access = new ObservableCatalogAccessService(holdChecks);

        context.Services.AddSingleton<ICatalogAccessService>(access);
        context.Services.AddSingleton<ISupabaseRestService>(new EmptySupabaseRestService());
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton<IAniListService>(new NoOpAniListService());
        context.Services.AddSingleton(new FranchiseService());
        context.Services.AddSingleton(sp => new FranchiseGapService(sp.GetRequiredService<IAniListEnrichmentService>()));
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>()));
        context.Services.AddSingleton<ICatalogService>(sp => sp.GetRequiredService<CatalogService>());
        context.Services.AddSingleton(sp => new AuthService(
            new HttpClient(new StubAdminHandler()),
            new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
            sp.GetRequiredService<NavigationManager>(),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "sb_publishable_123"
            }),
            new AppAuthenticationStateProvider()));
        context.Services.AddSingleton<IAuthStateNotifier>(sp => sp.GetRequiredService<AuthService>());
        context.Services.AddSingleton<IAdminAuthorizationService>(sp => sp.GetRequiredService<AuthService>());
        context.Services.AddSingleton(sp => new CatalogTransferService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<ICatalogService>(),
            sp.GetRequiredService<IAdminAuthorizationService>()));

        return context;
    }

    private static string SerializeUnexpiredSession()
    {
        return JsonSerializer.Serialize(
            new AuthSession
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                User = new AppUser { Id = "11111111-1111-1111-1111-111111111111" }
            },
            JsonDefaults.Web);
    }

    // Lets a test park the access check open and then prove the page's cancellation actually reached
    // it, which is the only observable difference between stopping the scan and merely hiding it.
    private sealed class ObservableCatalogAccessService : ICatalogAccessService
    {
        private readonly ManualResetEventSlim _checkStarted = new(false);
        private readonly bool _hold;

        public ObservableCatalogAccessService(bool hold) => _hold = hold;

        public bool HeldCheckWasCancelled { get; private set; }

        public void WaitUntilACheckIsInFlight() => Assert.True(_checkStarted.Wait(TimeSpan.FromSeconds(5)));

        public async Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
        {
            _checkStarted.Set();

            if (!_hold)
            {
                return true;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                HeldCheckWasCancelled = true;
                throw;
            }

            return true;
        }

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAdminHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/rest/v1/rpc/is_admin", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("true")
                });
            }

            if (path.EndsWith("/auth/v1/logout", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        }
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

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AniListMedia>>([]);
    }
}
