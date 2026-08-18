using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Components;

public sealed class AnimeSearchTests
{
    [Fact]
    public void ClickingAnotherResult_MovesSelectedState()
    {
        var service = CreateAdminCatalogService(new Dictionary<string, IReadOnlyList<AniListMedia>>
        {
            ["gundam"] =
            [
                CreateMedia(1, "Mobile Suit Gundam"),
                CreateMedia(2, "Mobile Suit Zeta Gundam")
            ]
        });

        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var selectedIds = new List<int>();
        var cut = context.Render<AnimeSearch>(parameters => parameters.Add(p => p.OnSelected, media => selectedIds.Add(media.Id)));

        cut.Find("input").Change("gundam");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".search-result").Count));

        var buttons = cut.FindAll(".search-result");
        buttons[0].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".search-result--selected"));
            Assert.Contains("search-result--selected", cut.FindAll(".search-result")[0].ClassList);
        });

        cut.FindAll(".search-result")[1].Click();

        cut.WaitForAssertion(() =>
        {
            var results = cut.FindAll(".search-result");
            Assert.Single(cut.FindAll(".search-result--selected"));
            Assert.DoesNotContain("search-result--selected", results[0].ClassList);
            Assert.Contains("search-result--selected", results[1].ClassList);
        });

        Assert.Equal([1, 2], selectedIds);
    }

    [Fact]
    public void FlagsOnlyResultsAlreadyInTheCatalog()
    {
        var service = CreateAdminCatalogService(new Dictionary<string, IReadOnlyList<AniListMedia>>
        {
            ["gundam"] =
            [
                CreateMedia(1, "Mobile Suit Gundam"),
                CreateMedia(2, "Mobile Suit Zeta Gundam")
            ]
        });

        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var cut = context.Render<AnimeSearch>(parameters => parameters
            .Add(p => p.CatalogedAniListIds, new Dictionary<int, long> { [2] = 55L }));

        cut.Find("input").Change("gundam");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".search-result").Count));

        var results = cut.FindAll(".search-result");
        Assert.DoesNotContain("search-result--cataloged", results[0].ClassList);
        Assert.Contains("search-result--cataloged", results[1].ClassList);
        Assert.Single(cut.FindAll(".search-result--cataloged"));
    }

    [Fact]
    public void NewSearch_ClearsSelectionWhenSelectedMediaDisappears()
    {
        var service = CreateAdminCatalogService(new Dictionary<string, IReadOnlyList<AniListMedia>>
        {
            ["fate"] =
            [
                CreateMedia(11, "Fate/stay night"),
                CreateMedia(22, "Fate/Zero")
            ],
            ["garden"] =
            [
                CreateMedia(22, "Fate/Zero"),
                CreateMedia(33, "Garden of Sinners")
            ]
        });

        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var cut = context.Render<AnimeSearch>();

        cut.Find("input").Change("fate");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".search-result").Count));

        cut.FindAll(".search-result")[0].Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".search-result--selected")));

        cut.Find("input").Change("garden");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".search-result").Count);
            Assert.Empty(cut.FindAll(".search-result--selected"));
        });
    }

    private static AdminCatalogService CreateAdminCatalogService(IReadOnlyDictionary<string, IReadOnlyList<AniListMedia>> searchResults)
    {
        return new AdminCatalogService(
            new StubSupabaseRestService(),
            new StubAniListService(searchResults),
            new StubAdminAuthorizationService(),
            new StubCatalogService());
    }

    private static AniListMedia CreateMedia(int id, string title)
    {
        return new AniListMedia
        {
            Id = id,
            Title = new AniListTitle
            {
                English = title,
                Romaji = title
            },
            Format = "TV",
            SeasonYear = 2026
        };
    }

    private sealed class StubAniListService : IAniListService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<AniListMedia>> _searchResults;

        public StubAniListService(IReadOnlyDictionary<string, IReadOnlyList<AniListMedia>> searchResults)
        {
            _searchResults = searchResults;
        }

        public Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_searchResults.TryGetValue(search, out var results)
                ? results
                : Array.Empty<AniListMedia>() as IReadOnlyList<AniListMedia>);
        }

        public Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubAdminAuthorizationService : IAdminAuthorizationService
    {
        public Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class StubCatalogService : ICatalogService
    {
        public bool IsConfigured => true;

        public Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSupabaseRestService : ISupabaseRestService
    {
        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc") => throw new NotSupportedException();

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
