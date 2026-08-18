using AnimeCatalog.Models;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class FranchiseServiceTests
{
    private readonly FranchiseService _service = new();

    [Fact]
    public void BuildCatalog_MatchesFranchiseWhenAnyEntryMatchesFilter()
    {
        var franchise = new Franchise { Id = 1, Title = "Attack on Titan", Slug = "attack-on-titan" };
        var entries =
            new[]
            {
                new AnimeEntry { Id = 10, FranchiseId = 1, TitleRomaji = "Shingeki no Kyojin", TitleEnglish = "Attack on Titan", DisplayOrder = 1, Episodes = 25 },
                new AnimeEntry { Id = 11, FranchiseId = 1, TitleRomaji = "Shingeki no Kyojin Season 2", TitleEnglish = "Attack on Titan Season 2", DisplayOrder = 2, Episodes = 12 }
            };
        var catalog =
            new[]
            {
                new CatalogEntry { AnimeEntryId = 10, Status = CatalogStatus.Completed, Score = 9.0m },
                new CatalogEntry { AnimeEntryId = 11, Status = CatalogStatus.Watching, Score = 8.0m }
            };

        var result = _service.BuildCatalog(entries, catalog, [], [franchise], new CatalogFilters
        {
            Status = CatalogStatus.Watching
        });

        Assert.Single(result);
        Assert.Equal(2, result[0].EntryCount);
        Assert.Single(result[0].VisibleEntries);
        Assert.Equal(11, result[0].VisibleEntries[0].AnimeEntry.Id);
        Assert.Equal(8.5m, result[0].AverageScore);
    }

    [Fact]
    public void BuildCatalog_ThrowsWhenCatalogEntryIsMissing()
    {
        var entries =
            new[]
            {
                new AnimeEntry
                {
                    Id = 4,
                    AniListId = 198113,
                    TitleRomaji = "Kill Ao",
                    TitleEnglish = "KILL BLUE",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.BuildCatalog(entries, [], [], [], new CatalogFilters()));

        Assert.Equal("Catalog entry for anime_entry_id=4 is missing.", exception.Message);
    }

    [Fact]
    public void BuildHomeSummary_OrdersRecentlyAddedNewestFirst()
    {
        var older = Item(1, "Older", CatalogStatus.Planned, createdAt: At(2026, 8, 15));
        var newer = Item(2, "Newer", CatalogStatus.Planned, createdAt: At(2026, 8, 16));

        var result = _service.BuildHomeSummary([Standalone(older, newer)], Now);

        Assert.Equal(2, result.RecentlyAdded.Count);
        Assert.Equal(2, result.RecentlyAdded[0].AnimeEntry.Id);
        Assert.Equal(1, result.RecentlyAdded[1].AnimeEntry.Id);
    }

    [Fact]
    public void BuildHomeSummary_StatusBreakdownCoversEveryStatusInEnumOrder()
    {
        var result = _service.BuildHomeSummary(
            [Standalone(Item(1, "A", CatalogStatus.Completed), Item(2, "B", CatalogStatus.Dropped))],
            Now);

        Assert.Equal(
            [CatalogStatus.Planned, CatalogStatus.Watching, CatalogStatus.Completed, CatalogStatus.OnHold, CatalogStatus.Dropped],
            result.StatusBreakdown.Select(segment => segment.Status));
        Assert.Equal(0, result.CountFor(CatalogStatus.Planned));
        Assert.Equal(1, result.CountFor(CatalogStatus.Completed));
        Assert.Equal(1, result.CountFor(CatalogStatus.Dropped));
        Assert.Equal(50, result.CompletionPercent);
    }

    [Fact]
    public void BuildHomeSummary_CountsOnlyRealFranchisesAndReportsStandalonesSeparately()
    {
        // A completed standalone entry arrives as its own pseudo-franchise. Counting it as a franchise
        // is what made the old "completed franchises" number misleading.
        var grouped = Grouped(7, "Monogatari", "monogatari", Item(1, "Bake", CatalogStatus.Completed));
        var loose = Standalone(Item(2, "One-off", CatalogStatus.Completed));

        var result = _service.BuildHomeSummary([grouped, loose], Now);

        Assert.Equal(1, result.FranchiseCount);
        Assert.Equal(1, result.StandaloneCount);
        Assert.Equal(1, result.CompletedFranchises);
        Assert.Equal(2, result.TotalEntries);
    }

    [Fact]
    public void BuildHomeSummary_TopFranchisesRankByCompletedCountAndAlwaysHaveASlug()
    {
        var one = Grouped(1, "One", "one", Item(1, "a", CatalogStatus.Completed));
        var two = Grouped(2, "Two", "two", Item(2, "b", CatalogStatus.Completed), Item(3, "c", CatalogStatus.Completed));
        var loose = Standalone(Item(4, "d", CatalogStatus.Completed));

        var result = _service.BuildHomeSummary([one, two, loose], Now);

        Assert.Equal(["two", "one"], result.TopFranchises.Select(item => item.Slug));
        Assert.All(result.TopFranchises, item => Assert.NotNull(item.Slug));
    }

    [Fact]
    public void BuildHomeSummary_SpotlightIsTheMostRecentlyUpdatedWatchingEntry()
    {
        var stale = Item(1, "Stale", CatalogStatus.Watching, updatedAt: At(2026, 8, 1));
        var fresh = Item(2, "Fresh", CatalogStatus.Watching, updatedAt: At(2026, 8, 17));
        var done = Item(3, "Done", CatalogStatus.Completed, updatedAt: At(2026, 8, 18));

        var result = _service.BuildHomeSummary([Standalone(stale, fresh, done)], Now);

        Assert.Equal(2, result.Spotlight?.AnimeEntry.Id);
        // The spotlight must not also appear in the list beneath it.
        Assert.Equal([1], result.ContinueWatching.Select(item => item.AnimeEntry.Id));
    }

    [Fact]
    public void BuildHomeSummary_SpotlightIsNullWhenNothingIsWatching()
    {
        var result = _service.BuildHomeSummary([Standalone(Item(1, "A", CatalogStatus.Planned))], Now);

        Assert.Null(result.Spotlight);
        Assert.Empty(result.ContinueWatching);
    }

    [Fact]
    public void BuildHomeSummary_ScoreDistributionFloorsScoresAndOmitsEmptyBuckets()
    {
        var entries = new[]
        {
            Item(1, "a", CatalogStatus.Completed, score: 10.0m),
            Item(2, "b", CatalogStatus.Completed, score: 9.5m),
            Item(3, "c", CatalogStatus.Completed, score: 9.0m),
            Item(4, "d", CatalogStatus.Completed, score: 8.9m),
            Item(5, "e", CatalogStatus.Completed)
        };

        var result = _service.BuildHomeSummary([Standalone(entries)], Now);

        Assert.Equal([(10, 1), (9, 2), (8, 1)], result.ScoreDistribution.Select(bucket => (bucket.Score, bucket.Count)));
        Assert.Equal(4, result.ScoredCount);
        Assert.Equal(10.0m, result.HighestScore);
        Assert.Equal(9.4m, result.AverageScore);
    }

    [Fact]
    public void BuildHomeSummary_ActivityWindowsUseTheSuppliedClock()
    {
        var entries = new[]
        {
            Completed(1, new DateOnly(2026, 8, 18)),   // today
            Completed(2, new DateOnly(2026, 7, 19)),   // exactly 30 days back
            Completed(3, new DateOnly(2026, 7, 18)),   // one day outside the window
            Completed(4, new DateOnly(2025, 12, 31))   // previous year
        };

        var result = _service.BuildHomeSummary([Standalone(entries)], Now);

        Assert.Equal(3, result.CompletedThisYear);
        Assert.Equal(2, result.CompletedLast30Days);
        Assert.Equal(4, result.RecentlyCompleted.Count);
        Assert.Equal(1, result.RecentlyCompleted[0].AnimeEntry.Id);
    }

    [Fact]
    public void BuildHomeSummary_UnknownEpisodeCountsAreFlaggedAndLeftOutOfTheTotal()
    {
        var known = Item(1, "Known", CatalogStatus.Completed, episodes: 12, episodesWatched: 12);
        var unknown = Item(2, "Unknown", CatalogStatus.Watching, episodes: null, episodesWatched: 3);

        var result = _service.BuildHomeSummary([Standalone(known, unknown)], Now);

        Assert.Equal(15, result.EpisodesWatched);
        Assert.Equal(12, result.EpisodesTotal);
        Assert.True(result.HasUnknownEpisodeCounts);
    }

    [Fact]
    public void BuildHomeSummary_HandlesAnEmptyCatalogWithoutDividingByZero()
    {
        var result = _service.BuildHomeSummary([], Now);

        Assert.Equal(0, result.TotalEntries);
        Assert.Equal(0, result.CompletionPercent);
        Assert.Null(result.AverageScore);
        Assert.Empty(result.ScoreDistribution);
        Assert.Equal(5, result.StatusBreakdown.Count);
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 10, 0, 0, TimeSpan.Zero);

    private static AnimeListItemViewModel Item(
        long id,
        string title,
        CatalogStatus status,
        decimal? score = null,
        int? episodes = null,
        int episodesWatched = 0,
        DateTimeOffset createdAt = default,
        DateTimeOffset updatedAt = default,
        DateOnly? completedAt = null) =>
        new()
        {
            AnimeEntry = new AnimeEntry { Id = id, AniListId = (int)id, TitleRomaji = title, Episodes = episodes },
            CatalogEntry = new CatalogEntry
            {
                AnimeEntryId = id,
                Status = status,
                Score = score,
                EpisodesWatched = episodesWatched,
                CompletedAt = completedAt,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            }
        };

    private static AnimeListItemViewModel Completed(long id, DateOnly completedAt) =>
        Item(id, $"Entry {id}", CatalogStatus.Completed, completedAt: completedAt);

    /// <summary>A pseudo-franchise, the shape BuildCatalog produces for ungrouped entries.</summary>
    private static FranchiseSummaryViewModel Standalone(params AnimeListItemViewModel[] entries) =>
        new()
        {
            Title = "Standalone",
            EntryCount = entries.Length,
            CompletedCount = entries.Count(entry => entry.CatalogEntry.Status == CatalogStatus.Completed),
            Entries = entries,
            VisibleEntries = entries
        };

    private static FranchiseSummaryViewModel Grouped(long id, string title, string slug, params AnimeListItemViewModel[] entries) =>
        new()
        {
            FranchiseId = id,
            Title = title,
            Slug = slug,
            EntryCount = entries.Length,
            CompletedCount = entries.Count(entry => entry.CatalogEntry.Status == CatalogStatus.Completed),
            Entries = entries,
            VisibleEntries = entries
        };
}
