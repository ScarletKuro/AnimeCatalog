using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class FranchiseEnrichmentTests
{
    private readonly FranchiseService _service = new();

    // ---- BuildFranchiseStats (Supabase only) --------------------------------

    [Fact]
    public void BuildFranchiseStats_CountsEveryStatusIncludingZeroes()
    {
        var stats = _service.BuildFranchiseStats(Summary(
            Entry(1, status: CatalogStatus.Completed),
            Entry(2, status: CatalogStatus.Completed),
            Entry(3, status: CatalogStatus.Watching)));

        Assert.Equal(Enum.GetValues<CatalogStatus>().Length, stats.StatusBreakdown.Count);
        Assert.Equal(2, stats.StatusBreakdown.Single(item => item.Status == CatalogStatus.Completed).Count);
        Assert.Equal(1, stats.StatusBreakdown.Single(item => item.Status == CatalogStatus.Watching).Count);
        Assert.Equal(0, stats.StatusBreakdown.Single(item => item.Status == CatalogStatus.Dropped).Count);
        Assert.True(stats.IsWatching);
    }

    [Fact]
    public void BuildFranchiseStats_WithNoEntriesReportsZeroCompletionInsteadOfDividingByZero()
    {
        var stats = _service.BuildFranchiseStats(Summary());

        Assert.Equal(0, stats.EntryCount);
        Assert.Equal(0, stats.CompletionPercent);
        Assert.Null(stats.AverageScore);
        Assert.Null(stats.YearSpan);
    }

    [Fact]
    public void BuildFranchiseStats_FlagsUnknownEpisodeCounts()
    {
        var stats = _service.BuildFranchiseStats(Summary(
            Entry(1, episodes: 12, watched: 12),
            Entry(2, episodes: null, watched: 3)));

        Assert.Equal(12, stats.EpisodesTotal);
        Assert.Equal(15, stats.EpisodesWatched);
        Assert.True(stats.HasUnknownEpisodeCounts);
    }

    [Fact]
    public void BuildFranchiseStats_SummarisesScores()
    {
        var stats = _service.BuildFranchiseStats(Summary(
            Entry(1, score: 9m),
            Entry(2, score: 7m),
            Entry(3)));

        Assert.Equal(2, stats.ScoredCount);
        Assert.Equal(8m, stats.AverageScore);
        Assert.Equal(9m, stats.HighestScore);
        Assert.Equal(7m, stats.LowestScore);
    }

    [Fact]
    public void BuildFranchiseStats_DerivesYearSpanFromSeasonYearOrStartDate()
    {
        var stats = _service.BuildFranchiseStats(Summary(
            Entry(1, seasonYear: 2014),
            Entry(2, startDate: new DateOnly(2011, 10, 2)),
            Entry(3)));

        Assert.Equal(2011, stats.FirstYear);
        Assert.Equal(2014, stats.LastYear);
        Assert.Equal("2011 – 2014", stats.YearSpan);
    }

    [Fact]
    public void BuildFranchiseStats_CollapsesASingleYearSpan()
    {
        var stats = _service.BuildFranchiseStats(Summary(Entry(1, seasonYear: 2014)));

        Assert.Equal("2014", stats.YearSpan);
    }

    // ---- BuildTimeline -----------------------------------------------------

    [Fact]
    public void BuildTimeline_OrdersYearsAscendingWithUnknownLast()
    {
        var groups = _service.BuildTimeline(
        [
            Entry(1, seasonYear: 2014),
            Entry(2),
            Entry(3, seasonYear: 2011)
        ]);

        Assert.Equal([2011, 2014, null], groups.Select(group => group.Year));
        Assert.Equal("Unknown", groups[^1].Label);
    }

    [Fact]
    public void BuildTimeline_OrdersWithinAYearBySeasonThenDisplayOrder()
    {
        var groups = _service.BuildTimeline(
        [
            Entry(1, seasonYear: 2014, season: "FALL", title: "Fall show"),
            Entry(2, seasonYear: 2014, season: "WINTER", title: "Winter show")
        ]);

        Assert.Equal(["Winter show", "Fall show"], groups.Single().Entries.Select(entry => entry.PrimaryTitle));
    }

    // ---- BuildFranchiseEnrichment (AniList) --------------------------------

    [Fact]
    public void BuildFranchiseEnrichment_RollsUpGenresByFrequencyThenName()
    {
        var entries = new[] { Entry(1, aniListId: 10), Entry(2, aniListId: 20) };
        var media = new Dictionary<int, AniListMedia>
        {
            [10] = new() { Id = 10, Genres = ["Action", "Drama"] },
            [20] = new() { Id = 20, Genres = ["Action", "Comedy"] }
        };

        var enrichment = _service.BuildFranchiseEnrichment(entries, media);

        Assert.Equal("Action", enrichment.Genres[0].Label);
        Assert.Equal(2, enrichment.Genres[0].Count);
        // Ties fall back to alphabetical order.
        Assert.Equal(["Comedy", "Drama"], enrichment.Genres.Skip(1).Select(item => item.Label));
    }

    [Fact]
    public void BuildFranchiseEnrichment_PicksBannerFromTheMostPopularEntry()
    {
        var entries = new[] { Entry(1, aniListId: 10), Entry(2, aniListId: 20) };
        var media = new Dictionary<int, AniListMedia>
        {
            [10] = new() { Id = 10, Popularity = 100, BannerImage = "ova-banner.jpg" },
            [20] = new() { Id = 20, Popularity = 90_000, BannerImage = "flagship-banner.jpg" }
        };

        var enrichment = _service.BuildFranchiseEnrichment(entries, media);

        Assert.Equal("flagship-banner.jpg", enrichment.BannerUrl);
    }

    [Fact]
    public void BuildFranchiseEnrichment_AveragesOnlyEntriesThatHaveACommunityScore()
    {
        var entries = new[] { Entry(1, aniListId: 10), Entry(2, aniListId: 20) };
        var media = new Dictionary<int, AniListMedia>
        {
            [10] = new() { Id = 10, AverageScore = 80 },
            [20] = new() { Id = 20, AverageScore = null }
        };

        var enrichment = _service.BuildFranchiseEnrichment(entries, media);

        Assert.Equal(80, enrichment.AniListAverageScore);
    }

    [Fact]
    public void BuildFranchiseEnrichment_ReportsPartialCoverage()
    {
        var entries = new[] { Entry(1, aniListId: 10), Entry(2, aniListId: 20) };
        var media = new Dictionary<int, AniListMedia> { [10] = new() { Id = 10 } };

        var enrichment = _service.BuildFranchiseEnrichment(entries, media);

        Assert.Equal(2, enrichment.EntryCount);
        Assert.Equal(1, enrichment.LoadedCount);
        Assert.True(enrichment.IsPartial);
        Assert.True(enrichment.HasAny);
    }

    [Fact]
    public void BuildFranchiseEnrichment_WithNoLoadedMediaReturnsEmptyRollupsWithoutThrowing()
    {
        var entries = new[] { Entry(1, aniListId: 10) };

        var enrichment = _service.BuildFranchiseEnrichment(entries, new Dictionary<int, AniListMedia>());

        Assert.Equal(0, enrichment.LoadedCount);
        Assert.False(enrichment.HasAny);
        Assert.Empty(enrichment.Genres);
        Assert.Null(enrichment.AniListAverageScore);
        Assert.Null(enrichment.BannerUrl);
    }

    // ---- BuildAnimeEnrichment ---------------------------------------------

    [Fact]
    public void BuildAnimeEnrichment_SeparatesSpoilerTagsAndSortsByRank()
    {
        var media = new AniListMedia
        {
            Id = 10,
            Tags =
            [
                new() { Name = "Time Skip", Rank = 60 },
                new() { Name = "Iyashikei", Rank = 90 },
                new() { Name = "Major Death", Rank = 80, IsMediaSpoiler = true }
            ]
        };

        var enrichment = _service.BuildAnimeEnrichment(media, localEpisodeCount: null);

        Assert.Equal(["Iyashikei", "Time Skip"], enrichment.Tags.Select(tag => tag.Name));
        Assert.Equal("Major Death", Assert.Single(enrichment.SpoilerTags).Name);
    }

    [Fact]
    public void BuildAnimeEnrichment_SanitizesTheDescription()
    {
        var media = new AniListMedia
        {
            Id = 10,
            Description = "A synopsis.<br><br><script>alert(1)</script>More."
        };

        var enrichment = _service.BuildAnimeEnrichment(media, localEpisodeCount: null);

        Assert.True(enrichment.HasDescription);
        Assert.DoesNotContain("<script", enrichment.Description.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A synopsis.", enrichment.Description.Html);
    }

    [Fact]
    public void BuildAnimeEnrichment_PutsAllTimeRankingsFirst()
    {
        var media = new AniListMedia
        {
            Id = 10,
            Rankings =
            [
                new() { Rank = 3, Context = "highest rated", Year = 2016, AllTime = false },
                new() { Rank = 42, Context = "most popular", AllTime = true }
            ]
        };

        var enrichment = _service.BuildAnimeEnrichment(media, localEpisodeCount: null);

        Assert.True(enrichment.Rankings[0].AllTime);
    }

    [Fact]
    public void BuildAnimeEnrichment_ComputesRuntimeFromTheLocalEpisodeCount()
    {
        var media = new AniListMedia { Id = 10, Episodes = 24, Duration = 24 };

        var enrichment = _service.BuildAnimeEnrichment(media, localEpisodeCount: 12);

        Assert.Equal(288, enrichment.TotalRuntimeMinutes);
    }

    [Fact]
    public void BuildAnimeEnrichment_LeavesRuntimeNullWhenDurationIsUnknown()
    {
        var media = new AniListMedia { Id = 10, Episodes = 24, Duration = null };

        var enrichment = _service.BuildAnimeEnrichment(media, localEpisodeCount: 24);

        Assert.Null(enrichment.TotalRuntimeMinutes);
    }

    private static FranchiseSummaryViewModel Summary(params AnimeListItemViewModel[] entries) => new()
    {
        Title = "Fate",
        EntryCount = entries.Length,
        Entries = entries,
        VisibleEntries = entries
    };

    private static AnimeListItemViewModel Entry(
        long id,
        int aniListId = 0,
        string? title = null,
        CatalogStatus status = CatalogStatus.Completed,
        decimal? score = null,
        int? episodes = null,
        int watched = 0,
        int? seasonYear = null,
        string? season = null,
        DateOnly? startDate = null) => new()
        {
            AnimeEntry = new AnimeEntry
            {
                Id = id,
                AniListId = aniListId == 0 ? (int)id : aniListId,
                TitleRomaji = title ?? $"Entry {id}",
                Episodes = episodes,
                SeasonYear = seasonYear,
                Season = season,
                StartDate = startDate
            },
            CatalogEntry = new CatalogEntry
            {
                AnimeEntryId = id,
                Status = status,
                Score = score,
                EpisodesWatched = watched
            }
        };
}
