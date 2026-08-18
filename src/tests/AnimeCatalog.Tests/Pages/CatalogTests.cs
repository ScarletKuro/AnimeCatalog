using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

public sealed class CatalogTests
{
    [Fact]
    public void FirstRender_ShowsOnlyLoading_NotTheFilters()
    {
        using var context = CreateContext(out var access);

        var cut = context.Render<Catalog>();

        // The access check is still in flight: nothing that implies a readable catalog may be
        // on screen yet, or a private catalog flashes a search bar before refusing access.
        Assert.Empty(cut.FindAll(".filters-card"));
        Assert.Contains("Loading catalog...", cut.Markup);

        access.Complete(canRead: false);
    }

    [Fact]
    public void DeniedAccess_ReplacesLoadingWithThePrivateCard_AndNeverShowsTheFilters()
    {
        using var context = CreateContext(out var access);

        var cut = context.Render<Catalog>();
        access.Complete(canRead: false);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(CatalogAccess.PrivateMessage, cut.Markup);
            Assert.Single(cut.FindAll(".access-card"));
        });

        Assert.Empty(cut.FindAll(".filters-card"));
    }

    [Fact]
    public void GrantedAccess_ShowsTheFiltersOnceTheLoadResolves()
    {
        using var context = CreateContext(out var access);

        var cut = context.Render<Catalog>();
        access.Complete(canRead: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".filters-card"));
            Assert.Contains("No entries match the current filters.", cut.Markup);
        });

        Assert.Empty(cut.FindAll(".access-card"));
    }

    private static BunitContext CreateContext(out GatedCatalogAccessService access)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        access = new GatedCatalogAccessService();
        var franchiseService = new FranchiseService();

        context.Services.AddSingleton<ICatalogAccessService>(access);
        context.Services.AddSingleton<ISupabaseRestService>(new EmptySupabaseRestService());
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton(franchiseService);
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>()));

        return context;
    }

    /// <summary>Holds the access check open so the first render can be inspected mid-load.</summary>
    private sealed class GatedCatalogAccessService : ICatalogAccessService
    {
        private readonly TaskCompletionSource<bool> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(bool canRead) => _gate.TrySetResult(canRead);

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => _gate.Task;

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

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
}
