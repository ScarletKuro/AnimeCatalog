using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

public sealed class CatalogAccessServiceTests
{
    [Fact]
    public async Task GetPublicCatalogEnabledAsync_ReadsSettingsRowOne()
    {
        var supabase = new FakeSupabaseRestService
        {
            SelectedSettingsRow = new AppSettingsRow
            {
                Id = 1,
                PublicCatalogEnabled = false
            }
        };

        var service = new CatalogAccessService(supabase, new FakeAdminAuthorizationService());

        var enabled = await service.GetPublicCatalogEnabledAsync();

        Assert.False(enabled);
        Assert.Equal("app_settings", supabase.LastSelectSingleTable);
        Assert.Equal("eq.1", supabase.LastSelectSingleQuery!["id"]);
    }

    [Fact]
    public async Task CanCurrentUserReadCatalogAsync_UsesRpc()
    {
        var supabase = new FakeSupabaseRestService
        {
            RpcResult = false
        };

        var service = new CatalogAccessService(supabase, new FakeAdminAuthorizationService());

        var canRead = await service.CanCurrentUserReadCatalogAsync();

        Assert.False(canRead);
        Assert.Equal("can_read_catalog", supabase.LastRpcFunctionName);
    }

    [Fact]
    public async Task SetPublicCatalogEnabledAsync_UpdatesSettingsRowOne()
    {
        var supabase = new FakeSupabaseRestService
        {
            UpdatedSettingsRow = new AppSettingsRow
            {
                Id = 1,
                PublicCatalogEnabled = true
            }
        };

        var service = new CatalogAccessService(supabase, new FakeAdminAuthorizationService());

        await service.SetPublicCatalogEnabledAsync(true);

        Assert.Equal("app_settings", supabase.LastUpdateTable);
        Assert.Equal("eq.1", supabase.LastUpdateQuery!["id"]);
        Assert.True((bool)supabase.LastUpdatePayload!.GetType().GetProperty("public_catalog_enabled")!.GetValue(supabase.LastUpdatePayload)!);
    }

    [Fact]
    public async Task GetPublicCatalogEnabledAsync_RequiresAdmin()
    {
        var service = new CatalogAccessService(new FakeSupabaseRestService(), new FakeAdminAuthorizationService(isAdmin: false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetPublicCatalogEnabledAsync());
    }

    private sealed class FakeAdminAuthorizationService : IAdminAuthorizationService
    {
        private readonly bool _isAdmin;

        public FakeAdminAuthorizationService(bool isAdmin = true)
        {
            _isAdmin = isAdmin;
        }

        public Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_isAdmin);
    }

    private sealed class FakeSupabaseRestService : ISupabaseRestService
    {
        public bool IsConfigured => true;

        public string? LastSelectSingleTable { get; private set; }
        public IReadOnlyDictionary<string, string>? LastSelectSingleQuery { get; private set; }
        public string? LastUpdateTable { get; private set; }
        public IReadOnlyDictionary<string, string>? LastUpdateQuery { get; private set; }
        public object? LastUpdatePayload { get; private set; }
        public string? LastRpcFunctionName { get; private set; }
        public AppSettingsRow? SelectedSettingsRow { get; init; }
        public AppSettingsRow? UpdatedSettingsRow { get; init; }
        public bool RpcResult { get; init; } = true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => throw new NotSupportedException();

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
        {
            LastSelectSingleTable = table;
            LastSelectSingleQuery = query;
            return Task.FromResult((T?)(object?)SelectedSettingsRow);
        }

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
        {
            LastUpdateTable = table;
            LastUpdateQuery = query;
            LastUpdatePayload = payload;
            return Task.FromResult((T?)(object?)UpdatedSettingsRow);
        }

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
        {
            LastRpcFunctionName = functionName;
            return Task.FromResult((T?)(object?)RpcResult);
        }
    }
}
