using AnimeCatalog.Infrastructure;
using AnimeCatalog.Options;
using AnimeCatalog.Pages.Admin;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

public sealed class AdminDashboardTests
{
    [Fact]
    public void AnonymousVisitor_SeesNoAdminPanels()
    {
        using var context = CreateContext();

        var cut = context.Render<Dashboard>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Authentication required", cut.Markup);
            Assert.DoesNotContain("Catalog visibility", cut.Markup);
            Assert.DoesNotContain("Backup and restore", cut.Markup);
        });
    }

    [Fact]
    public void AnonymousVisitor_StillGetsTheFocusableHeading()
    {
        using var context = CreateContext();

        var cut = context.Render<Dashboard>();

        // Routes.razor focuses the h1 after navigation, so the header has to survive every branch.
        cut.WaitForAssertion(() => Assert.Contains("Catalog maintenance", cut.Find("h1").TextContent));
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton<IAuthStateNotifier>(sp => sp.GetRequiredService<AuthService>());
        context.Services.AddSingleton<ICatalogAccessService>(new UnusedCatalogAccessService());
        context.Services.AddSingleton<ISupabaseRestService>(new UnusedSupabaseRestService());
        context.Services.AddSingleton(new FranchiseService());
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

        return context;
    }

    private sealed class UnusedCatalogAccessService : ICatalogAccessService
    {
        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An anonymous dashboard must not reach the access check.");

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An anonymous dashboard must not read app_settings.");

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An anonymous dashboard must not write app_settings.");
    }

    private sealed class UnusedSupabaseRestService : ISupabaseRestService
    {
        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => throw new InvalidOperationException($"Unexpected SelectAsync on {table}");

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
