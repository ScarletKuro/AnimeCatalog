using System.Net;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task GetAdminDashboardAsync_IncludesPublicCatalogEnabledFlag()
    {
        var service = new CatalogService(
            new FakeSupabaseRestService(
                animeRows: [new AnimeEntryRow { Id = 1, AniListId = 100, TitleRomaji = "A", DisplayOrder = 0 }],
                catalogRows: [new CatalogEntryRow { Id = 1, AnimeEntryId = 1, Status = "watching", EpisodesWatched = 1 }],
                relationRows: [],
                franchiseRows: []),
            new FranchiseService(),
            new FakeCatalogAccessService(publicCatalogEnabled: false));

        var summary = await service.GetAdminDashboardAsync();

        Assert.False(summary.PublicCatalogEnabled);
        Assert.Equal(1, summary.AnimeEntryCount);
        Assert.Equal(1, summary.WatchingCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsForbiddenReadToCatalogAccessDenied()
    {
        var service = new CatalogService(
            new ThrowingSupabaseRestService(new PostgrestException(new PostgrestError { Message = "forbidden", Code = "42501" }, (int)HttpStatusCode.Forbidden)),
            new FranchiseService(),
            new FakeCatalogAccessService());

        await Assert.ThrowsAsync<CatalogAccessDeniedException>(() => service.GetSnapshotAsync());
    }

    [Fact]
    public async Task GetSnapshotAsync_ThrowsCatalogAccessDeniedWhenRpcSaysCatalogIsPrivate()
    {
        var service = new CatalogService(
            new FakeSupabaseRestService([], [], [], []),
            new FranchiseService(),
            new FakeCatalogAccessService(publicCatalogEnabled: false, canReadCatalog: false));

        await Assert.ThrowsAsync<CatalogAccessDeniedException>(() => service.GetSnapshotAsync());
    }

    private sealed class FakeCatalogAccessService : ICatalogAccessService
    {
        private readonly bool _publicCatalogEnabled;
        private readonly bool _canReadCatalog;

        public FakeCatalogAccessService(bool publicCatalogEnabled = true, bool canReadCatalog = true)
        {
            _publicCatalogEnabled = publicCatalogEnabled;
            _canReadCatalog = canReadCatalog;
        }

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_canReadCatalog);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_publicCatalogEnabled);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingSupabaseRestService : ISupabaseRestService
    {
        private readonly Exception _exception;

        public ThrowingSupabaseRestService(Exception exception)
        {
            _exception = exception;
        }

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

    private sealed class FakeSupabaseRestService : ISupabaseRestService
    {
        private readonly IReadOnlyList<AnimeEntryRow> _animeRows;
        private readonly IReadOnlyList<CatalogEntryRow> _catalogRows;
        private readonly IReadOnlyList<AnimeRelationRow> _relationRows;
        private readonly IReadOnlyList<FranchiseRow> _franchiseRows;

        public FakeSupabaseRestService(
            IReadOnlyList<AnimeEntryRow> animeRows,
            IReadOnlyList<CatalogEntryRow> catalogRows,
            IReadOnlyList<AnimeRelationRow> relationRows,
            IReadOnlyList<FranchiseRow> franchiseRows)
        {
            _animeRows = animeRows;
            _catalogRows = catalogRows;
            _relationRows = relationRows;
            _franchiseRows = franchiseRows;
        }

        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
        {
            IEnumerable<T> rows = table switch
            {
                "anime_entries" => _animeRows.Cast<T>(),
                "catalog_entries" => _catalogRows.Cast<T>(),
                "anime_relations" => _relationRows.Cast<T>(),
                "franchises" => _franchiseRows.Cast<T>(),
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
}
