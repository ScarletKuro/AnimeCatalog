using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Options;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

public sealed class PrivateCatalogPageTests
{
    [Fact]
    public async Task Home_ShowsPrivateCatalogState_WhenCatalogAccessIsDenied()
    {
        await using var context = CreateContext();

        var cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateTitle, cut.Markup);
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.DoesNotContain("Nothing in the catalog yet.", cut.Markup);
        });
    }

    [Fact]
    public async Task Catalog_ShowsPrivateCatalogState_WhenCatalogAccessIsDenied()
    {
        await using var context = CreateContext("catalog");

        var cut = context.Render<Catalog>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateTitle, cut.Markup);
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.DoesNotContain("No entries match the current filters.", cut.Markup);
            Assert.Contains("login?returnUrl=catalog", cut.Markup);
        });
    }

    [Fact]
    public async Task Franchise_ShowsPrivateCatalogState_WhenCatalogAccessIsDenied()
    {
        await using var context = CreateContext("franchise/gundam");

        var cut = context.Render<Franchise>(parameters => parameters.Add(p => p.Slug, "gundam"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateTitle, cut.Markup);
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.Contains("login?returnUrl=franchise%2Fgundam", cut.Markup);
        });
    }

    [Fact]
    public async Task AnimeDetails_ShowsPrivateCatalogState_WhenCatalogAccessIsDenied()
    {
        await using var context = CreateContext("anime/174");

        var cut = context.Render<AnimeDetails>(parameters => parameters.Add(p => p.Id, 174L));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateTitle, cut.Markup);
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.Contains("login?returnUrl=anime%2F174", cut.Markup);
        });
    }

    private static BunitContext CreateContext(string relativeUri = "")
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var accessService = new DeniedCatalogAccessService();
        var supabase = new UnexpectedReadSupabaseRestService();
        var franchiseService = new FranchiseService();

        context.Services.AddSingleton<ICatalogAccessService>(accessService);
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        context.Services.AddSingleton<ISupabaseRestService>(supabase);
        context.Services.AddSingleton<IAniListEnrichmentService, NoOpAniListEnrichmentService>();
        context.Services.AddSingleton(franchiseService);
        context.Services.AddSingleton<IAniListService, NoOpAniListService>();
        context.Services.AddSingleton<IAdminAuthorizationService, AlwaysAdminAuthorizationService>();
        context.Services.AddSingleton(sp =>
            new CatalogService(
                sp.GetRequiredService<ISupabaseRestService>(),
                sp.GetRequiredService<FranchiseService>(),
                sp.GetRequiredService<ICatalogAccessService>()));
        context.Services.AddSingleton(sp =>
            new AuthService(
                new HttpClient(),
                new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
                sp.GetRequiredService<NavigationManager>(),
                Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
                {
                    Url = "https://example.supabase.co",
                    PublishableKey = "sb_publishable_123"
                }),
                new AppAuthenticationStateProvider()));
        context.Services.AddSingleton(sp =>
            new AdminCatalogService(
                sp.GetRequiredService<ISupabaseRestService>(),
                sp.GetRequiredService<IAniListService>(),
                sp.GetRequiredService<IAdminAuthorizationService>(),
                sp.GetRequiredService<CatalogService>()));

        context.Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUri);

        return context;
    }

    private sealed class DeniedCatalogAccessService : ICatalogAccessService
    {
        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class UnexpectedReadSupabaseRestService : ISupabaseRestService
    {
        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => throw new InvalidOperationException($"Catalog pages should not reach table reads when can_read_catalog is false. Unexpected table: {table}");

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected SelectSingleAsync on {table}");

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected InsertSingleAsync on {table}");

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected InsertManyAsync on {table}");

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected UpsertSingleAsync on {table}");

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected UpdateSingleAsync on {table}");

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected DeleteAsync on {table}");

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Unexpected RpcAsync on {functionName}");
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
