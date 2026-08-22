using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

public sealed class CatalogTests
{
    [Fact]
    public async Task FirstRender_ShowsOnlyLoading_NotTheFilters()
    {
        await using var context = CreateContext(out var access);

        var cut = context.Render<Catalog>();

        // The access check is still in flight: nothing that implies a readable catalog may be
        // on screen yet, or a private catalog flashes a search bar before refusing access.
        Assert.Empty(cut.FindAll(".filters-card"));
        Assert.Contains("Loading catalog...", cut.Markup);

        access.Complete(canRead: false);
    }

    [Fact]
    public async Task DeniedAccess_ReplacesLoadingWithThePrivateCard_AndNeverShowsTheFilters()
    {
        await using var context = CreateContext(out var access);

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
    public async Task GrantedAccess_ShowsTheFiltersOnceTheLoadResolves()
    {
        await using var context = CreateContext(out var access);

        var cut = context.Render<Catalog>();
        access.Complete(canRead: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".filters-card"));
            Assert.Contains("No entries match the current filters.", cut.Markup);
        });

        Assert.Empty(cut.FindAll(".access-card"));
    }

    [Fact]
    public async Task TheFirstPageShowsAtMostFortyEightCards()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context);

        Assert.Equal(48, cut.FindAll(".franchise-card").Count);
    }

    [Fact]
    public async Task APageQueryParameterSelectsThatSliceOfTheList()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context, "catalog?page=3");

        var cards = cut.FindAll(".franchise-card__title");
        Assert.Equal(34, cards.Count);
        Assert.Equal(TitleFor(97), cards[0].TextContent);
    }

    // The reason pagination is worth having at all: the whole catalog is already in memory, so
    // stepping through it must not go back to Supabase. If the page ever joins the filter key that
    // gates LoadAsync, this is what says so.
    [Fact]
    public async Task ChangingThePageDoesNotRefetchTheCatalog()
    {
        await using var context = CreateSeededContext(130, out var supabase);

        var cut = RenderCatalog(context);
        var readsAfterFirstLoad = supabase.SelectCount;

        cut.Find(".catalog-pager [aria-label='Next page']").Click();

        Assert.Equal(TitleFor(49), cut.FindAll(".franchise-card__title")[0].TextContent);
        Assert.Equal(readsAfterFirstLoad, supabase.SelectCount);
    }

    // The other half of the pair, so nobody widens that gate into a stale cache: a filter change
    // really does have to go back for a differently filtered list.
    [Fact]
    public async Task ChangingAFilterDoesRefetchTheCatalog()
    {
        await using var context = CreateSeededContext(130, out var supabase);

        var cut = RenderCatalog(context);
        var readsAfterFirstLoad = supabase.SelectCount;

        cut.Find(".filters-card input").Input(TitleFor(7));

        cut.WaitForAssertion(() => Assert.True(supabase.SelectCount > readsAfterFirstLoad));
    }

    [Fact]
    public async Task ClickingNextPushesThePageIntoTheUrl()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context);
        cut.Find(".catalog-pager [aria-label='Next page']").Click();

        // No "?q=&status=&sort=Title" noise alongside it: a default that is spelled out is a default
        // somebody has to read past.
        Assert.EndsWith("catalog?page=2", navigation.Uri);
    }

    [Fact]
    public async Task TheFirstPageIsNotSpelledOutInTheUrl()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?page=2");
        cut.Find(".catalog-pager [aria-label='Previous page']").Click();

        Assert.EndsWith("catalog", navigation.Uri);
    }

    [Fact]
    public async Task EveryPageIsListedWhenThereAreFewEnoughOfThem()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context);

        Assert.Equal(["1", "2", "3"], cut.FindAll(".catalog-pager__page").Select(page => page.TextContent));
        Assert.Empty(cut.FindAll(".catalog-pager__gap"));
    }

    [Fact]
    public async Task TheCurrentPageIsMarkedForAssistiveTechnologyAndNotJustStyled()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context, "catalog?page=2");

        var current = cut.Find(".catalog-pager__page--current");
        Assert.Equal("2", current.TextContent);
        Assert.Equal("page", current.GetAttribute("aria-current"));
        Assert.Single(cut.FindAll(".catalog-pager__page--current"));
    }

    [Fact]
    public async Task ClickingANumberGoesStraightToThatPage()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context);
        cut.Find(".catalog-pager [aria-label='Page 3']").Click();

        Assert.EndsWith("catalog?page=3", navigation.Uri);
        Assert.Equal(TitleFor(97), cut.FindAll(".franchise-card__title")[0].TextContent);
    }

    [Fact]
    public async Task TheLastPageIsOneClickAwayFromTheFirst()
    {
        await using var context = CreateSeededContext(1000, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context);
        cut.Find(".catalog-pager [aria-label='Last page']").Click();

        Assert.EndsWith("catalog?page=21", navigation.Uri);

        cut.Find(".catalog-pager [aria-label='First page']").Click();

        Assert.EndsWith("catalog", navigation.Uri);
    }

    // Both ends stay listed however deep in the catalog the visitor is, so the strip is a map of the
    // whole list rather than a window onto part of it.
    [Fact]
    public async Task ALongCatalogElidesTheMiddleButKeepsBothEnds()
    {
        await using var context = CreateSeededContext(1000, out _);

        var cut = RenderCatalog(context, "catalog?page=11");

        Assert.Equal(
            ["1", "9", "10", "11", "12", "13", "21"],
            cut.FindAll(".catalog-pager__page").Select(page => page.TextContent));
        Assert.Equal(2, cut.FindAll(".catalog-pager__gap").Count);
    }

    // A single hidden number is filled in rather than elided: the ellipsis would be wider than the
    // page it hides and would cost a click to resolve.
    [Fact]
    public async Task ASingleSkippedPageIsShownInsteadOfAnEllipsis()
    {
        await using var context = CreateSeededContext(1000, out _);

        var cut = RenderCatalog(context, "catalog?page=4");

        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "21"],
            cut.FindAll(".catalog-pager__page").Select(page => page.TextContent));
        Assert.Single(cut.FindAll(".catalog-pager__gap"));
    }

    [Fact]
    public async Task TheStepsAreDisabledAtTheEndsOfTheList()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context);

        Assert.True(cut.Find(".catalog-pager [aria-label='First page']").HasAttribute("disabled"));
        Assert.True(cut.Find(".catalog-pager [aria-label='Previous page']").HasAttribute("disabled"));
        Assert.False(cut.Find(".catalog-pager [aria-label='Next page']").HasAttribute("disabled"));
        Assert.False(cut.Find(".catalog-pager [aria-label='Last page']").HasAttribute("disabled"));
    }

    // Reachable by narrowing the filters while deep in the list, or by editing the address. The
    // clamp has to wait for the load to resolve: before that the count belongs to the previous
    // filter set, and clamping against it would rewrite the address twice.
    [Fact]
    public async Task AnOutOfRangePageIsRewrittenToTheLastPage()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?page=99");

        Assert.EndsWith("catalog?page=3", navigation.Uri);
        Assert.Equal(34, cut.FindAll(".franchise-card").Count);
    }

    [Fact]
    public async Task AnOutOfRangePageWithNoResultsFallsBackToTheFirstPage()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?page=99&q=nothingmatchesthis");

        Assert.EndsWith("catalog?q=nothingmatchesthis", navigation.Uri);
        Assert.Single(cut.FindAll(".empty-card"));
    }

    // Regression: bound as an int?, an unparsable page throws inside SetParametersAsync, and there
    // is no ErrorBoundary anywhere in this app to catch it - the visitor gets the reload banner
    // instead of a catalog. Every query parameter on this page is a string for that reason.
    [Fact]
    public async Task AnUnparsablePageQueryRendersTheFirstPageInsteadOfCrashing()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?page=abc");

        Assert.EndsWith("catalog", navigation.Uri);
        Assert.Equal(TitleFor(1), cut.FindAll(".franchise-card__title")[0].TextContent);
    }

    // Same hazard from the other direction: CatalogStatusExtensions.Parse throws, so the page has to
    // reach it through TryParse.
    [Fact]
    public async Task AnUnknownStatusQueryIsDroppedInsteadOfThrowing()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?status=bogus");

        Assert.EndsWith("catalog", navigation.Uri);
        Assert.Equal(string.Empty, cut.Find(".filters-card select").GetAttribute("value"));
    }

    [Fact]
    public async Task ALowercaseSortQueryIsAcceptedAndCanonicalised()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?sort=year");

        Assert.EndsWith("catalog?sort=Year", navigation.Uri);
        Assert.Equal("Year", cut.FindAll(".filters-card select")[1].GetAttribute("value"));
    }

    [Fact]
    public async Task TheUrlPreselectsTheFilterControls()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context, $"catalog?q={TitleFor(7)}&status=watching");

        Assert.Equal(TitleFor(7), cut.Find(".filters-card input").GetAttribute("value"));
        Assert.Equal("watching", cut.Find(".filters-card select").GetAttribute("value"));
    }

    [Fact]
    public async Task TypingReplacesTheHistoryEntryAndReturnsToPageOne()
    {
        await using var context = CreateSeededContext(130, out _);
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = RenderCatalog(context, "catalog?page=3");
        cut.Find(".filters-card input").Input(TitleFor(7));

        cut.WaitForAssertion(() => Assert.EndsWith($"catalog?q={TitleFor(7)}", navigation.Uri));
    }

    [Fact]
    public async Task NoPagerRendersWhenEverythingFitsOnOnePage()
    {
        await using var context = CreateSeededContext(10, out _);

        var cut = RenderCatalog(context);

        Assert.Empty(cut.FindAll(".catalog-pager"));
        // A range is only worth stating when something is being left out.
        Assert.Equal("10 results.", cut.Find(".catalog-toolbar__status").TextContent);
    }

    [Fact]
    public async Task TheStatusLineNamesTheVisibleRangeAndTheTotal()
    {
        await using var context = CreateSeededContext(130, out _);

        var cut = RenderCatalog(context, "catalog?page=2");

        Assert.Equal("Showing 49-96 of 130 results.", cut.Find(".catalog-toolbar__status").TextContent);
    }

    // Regression: with no @key on the grid loop Blazor matches FranchiseCard instances positionally,
    // so re-sorting left an expanded panel attached to whichever franchise landed in that slot.
    [Fact]
    public async Task AnExpandedPanelStaysWithItsOwnCardWhenTheSortChanges()
    {
        await using var context = CreateSeededContext(10, out _);

        var cut = RenderCatalog(context);
        var expandedTitle = cut.FindAll(".franchise-card__title")[0].TextContent;
        cut.FindAll(".franchise-card__toggle")[0].Click();

        // Seeded so that sorting by year exactly reverses the title order: a sort that left the grid
        // alone could not tell a keyed loop from an unkeyed one.
        cut.FindAll(".filters-card select")[1].Change(nameof(CatalogSortOption.Year));

        cut.WaitForAssertion(() =>
        {
            var expanded = cut.Find(".franchise-card--expanded");
            Assert.Equal(expandedTitle, expanded.QuerySelector(".franchise-card__title")!.TextContent);
        });
    }

    private static IRenderedComponent<Catalog> RenderCatalog(BunitContext context, string url = "catalog")
    {
        // Navigated before rendering rather than only parameterised: a component rendered directly
        // sits at the base address, and the page reads all four of its values out of the query.
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(url);

        var cut = context.Render<Catalog>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".filters-card")));
        return cut;
    }

    /// <summary>
    /// A catalog of <paramref name="count"/> standalone entries - one card each, titled so that the
    /// default title sort puts them in a predictable order.
    /// </summary>
    private static BunitContext CreateSeededContext(int count, out SeededSupabaseRestService supabase)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        supabase = new SeededSupabaseRestService(
            Enumerable.Range(1, count).Select(index => new AnimeEntryRow
            {
                Id = index,
                AniListId = index,
                TitleRomaji = TitleFor(index),
                Episodes = 12,
                DisplayOrder = 1,
                // Ascending with the title, so a descending year sort is the exact reverse of the
                // default title sort. Tests that need the grid to move rely on that.
                SeasonYear = 2000 + index
            }).ToList(),
            Enumerable.Range(1, count).Select(index => new CatalogEntryRow
            {
                Id = index,
                AnimeEntryId = index,
                Status = CatalogStatus.Watching.ToApiValue(),
                EpisodesWatched = 3
            }).ToList());

        context.Services.AddSingleton<IAuthStateNotifier>(new StubAuthStateNotifier());
        context.Services.AddSingleton<ICatalogAccessService>(new OpenCatalogAccessService());
        context.Services.AddSingleton<ISupabaseRestService>(supabase);
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton(new FranchiseService());
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>()));
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));

        return context;
    }

    /// <summary>Zero-padded so an ordinal title sort and a numeric one agree.</summary>
    private static string TitleFor(int index) => $"Title{index:000}";

    private static BunitContext CreateContext(out GatedCatalogAccessService access)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        access = new GatedCatalogAccessService();
        var franchiseService = new FranchiseService();

        context.Services.AddSingleton<IAuthStateNotifier>(new StubAuthStateNotifier());
        context.Services.AddSingleton<ICatalogAccessService>(access);
        context.Services.AddSingleton<ISupabaseRestService>(new EmptySupabaseRestService());
        context.Services.AddSingleton<IAniListEnrichmentService>(new NoOpAniListEnrichmentService());
        context.Services.AddSingleton(franchiseService);
        context.Services.AddSingleton(sp => new CatalogService(
            sp.GetRequiredService<ISupabaseRestService>(),
            sp.GetRequiredService<FranchiseService>(),
            sp.GetRequiredService<ICatalogAccessService>()));
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));

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

    /// <summary>Grants access without a gate, for the tests that are about paging rather than access.</summary>
    private sealed class OpenCatalogAccessService : ICatalogAccessService
    {
        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> GetPublicCatalogEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetPublicCatalogEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Serves a fixed catalog and counts the reads, so a test can assert that stepping pages costs
    /// nothing. <see cref="SelectCount"/> counts every table, which is the honest measure: one call
    /// to GetSnapshotAsync reads four of them.
    /// </summary>
    private sealed class SeededSupabaseRestService : ISupabaseRestService
    {
        private readonly IReadOnlyList<AnimeEntryRow> _animeRows;
        private readonly IReadOnlyList<CatalogEntryRow> _catalogRows;

        public SeededSupabaseRestService(IReadOnlyList<AnimeEntryRow> animeRows, IReadOnlyList<CatalogEntryRow> catalogRows)
        {
            _animeRows = animeRows;
            _catalogRows = catalogRows;
        }

        public int SelectCount { get; private set; }

        public bool IsConfigured => true;

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
        {
            SelectCount++;

            IEnumerable<T> rows = table switch
            {
                "anime_entries" => _animeRows.Cast<T>(),
                "catalog_entries" => _catalogRows.Cast<T>(),
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

    private sealed class NoOpAniListEnrichmentService : IAniListEnrichmentService
    {
        public Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(null);

        public Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(IReadOnlyCollection<int> aniListIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, AniListMedia>>(new Dictionary<int, AniListMedia>());
    }
}
