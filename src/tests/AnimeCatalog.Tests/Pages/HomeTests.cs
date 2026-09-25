using AnimeCatalog.Models.AniList;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Pages;

public sealed class HomeTests
{
    [Fact]
    public async Task AlsoWatching_LinksToTheWatchingCatalogFilter()
    {
        await using var context = CreateContext();

        var cut = context.Render<Home>();

        await cut.WaitForAssertionAsync(() =>
        {
            var link = cut.FindAll("#home-watching ~ a")
                .Single(anchor => anchor.TextContent.Contains("All watching", StringComparison.Ordinal));

            Assert.Equal("catalog?status=watching", link.GetAttribute("href"));
            Assert.Contains("button--inline", link.GetAttribute("class"));
        });
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

        context.Services.AddSingleton<IAuthStateNotifier>(new StubAuthStateNotifier());
        context.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        context.Services.AddSingleton<ICatalogAccessService>(new OpenCatalogAccessService());
        context.Services.AddSingleton<ISupabaseRestService>(new SeededSupabaseRestService(now));
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton(new FranchiseService());
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IAuthStateNotifier>(),
            sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()));

        return context;
    }

    private sealed class OpenCatalogAccessService : ICatalogAccessService
    {
        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SeededSupabaseRestService : ISupabaseRestService
    {
        private readonly DateTimeOffset _now;

        public SeededSupabaseRestService(DateTimeOffset now)
        {
            _now = now;
        }

        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(
            string table,
            IReadOnlyDictionary<string, string>? query = null,
            string select = "*",
            CancellationToken cancellationToken = default,
            string? order = "id.asc")
        {
            IEnumerable<T> rows = table switch
            {
                "anime_entries" => AnimeRows().Cast<T>(),
                "catalog_entries" => CatalogRows().Cast<T>(),
                "anime_relations" => [],
                "franchises" => [],
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

        private IReadOnlyList<AnimeEntryRow> AnimeRows() =>
        [
            Entry(1, "Fresh show", episodes: 12),
            Entry(2, "Backlog show", episodes: 24)
        ];

        private IReadOnlyList<CatalogEntryRow> CatalogRows() =>
        [
            CatalogEntry(1, animeEntryId: 1, updatedAt: _now),
            CatalogEntry(2, animeEntryId: 2, updatedAt: _now.AddMinutes(-5))
        ];

        private static AnimeEntryRow Entry(long id, string title, int episodes) => new()
        {
            Id = id,
            AniListId = 1000 + (int)id,
            TitleRomaji = title,
            Episodes = episodes,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        private static CatalogEntryRow CatalogEntry(long id, long animeEntryId, DateTimeOffset updatedAt) => new()
        {
            Id = id,
            AnimeEntryId = animeEntryId,
            Status = "watching",
            EpisodesWatched = 3,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
    }

    private sealed class NoOpAniListEnrichmentService : IAniListEnrichmentService
    {
        public Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(IReadOnlyCollection<int> aniListIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, AniListMedia>>(new Dictionary<int, AniListMedia>());
    }
}
