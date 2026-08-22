using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class CalendarServiceTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test/Plus3", TimeSpan.FromHours(3), "Test +3", "Test +3");

    private static readonly AiringWeek Week = AiringWeek.Containing(new DateOnly(2026, 8, 17));

    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SevenDaysAreAlwaysBuilt_EvenForAnEmptyWeek()
    {
        var week = Build(AiringScheduleLoad.Empty);

        Assert.Equal(7, week.Days.Count);
        Assert.Equal(new DateOnly(2026, 8, 17), week.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 8, 23), week.Days[6].Date);
        Assert.All(week.Days, day => Assert.Empty(day.Episodes));
    }

    [Fact]
    public void EpisodesLandInTheirLocalDayColumn()
    {
        // 21:00 UTC on the Monday is 00:00 Tuesday in +03:00, so it belongs to Tuesday.
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 17, 21, 0, 0, TimeSpan.Zero)),
            Schedule(2, new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero)));

        var week = Build(load);

        Assert.Equal([2], week.Days[0].Episodes.Select(episode => episode.AniListId).ToArray());
        Assert.Equal([1], week.Days[1].Episodes.Select(episode => episode.AniListId).ToArray());
    }

    [Fact]
    public void EpisodesWithinADayAreOrderedByTime()
    {
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 18, 18, 0, 0, TimeSpan.Zero)),
            Schedule(2, new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero)),
            Schedule(3, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));

        var week = Build(load);

        Assert.Equal([2, 3, 1], week.Days[1].Episodes.Select(episode => episode.AniListId).ToArray());
    }

    // The padded query window reaches past the week on purpose, so the surplus has to be dropped.
    [Fact]
    public void EpisodesInThePaddingAreDiscarded()
    {
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero)),   // 22:00 local Sunday, outside
            Schedule(2, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));  // inside

        var week = Build(load);

        Assert.Equal(1, week.LoadedEpisodeCount);
        Assert.Equal([2], week.Days.SelectMany(day => day.Episodes).Select(episode => episode.AniListId).ToArray());
    }

    [Fact]
    public void AnExactDuplicateIsDropped()
    {
        var airsAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var load = Load(Schedule(1, airsAt, episode: 5), Schedule(1, airsAt, episode: 5));

        var week = Build(load);

        Assert.Equal(1, week.LoadedEpisodeCount);
    }

    // A rebroadcast is the same series and episode at a different time, and it genuinely airs twice.
    [Fact]
    public void ARebroadcastIsKeptOnBothDays()
    {
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero), episode: 5),
            Schedule(1, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), episode: 5));

        var week = Build(load);

        Assert.Equal(2, week.LoadedEpisodeCount);
        Assert.Single(week.Days[1].Episodes);
        Assert.Single(week.Days[3].Episodes);
    }

    // One stale schedule row must not take the whole week down.
    [Fact]
    public void AScheduleRowWithNoMediaIsSkipped()
    {
        var orphan = Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        orphan.Media = null;

        var week = Build(Load(orphan, Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero))));

        Assert.Equal(1, week.LoadedEpisodeCount);
    }

    [Fact]
    public void TodayIsMarkedOnExactlyOneDay()
    {
        var week = Build(AiringScheduleLoad.Empty);

        var today = Assert.Single(week.Days, day => day.IsToday);
        Assert.Equal(new DateOnly(2026, 8, 19), today.Date);
    }

    [Fact]
    public void CatalogedTitlesCarryTheirStatusProgressAndAnInternalLink()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 1, CatalogStatus.Watching, 3, 8m, 12));
        var load = Load(Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero), episode: 5));

        var episode = Build(load, overlay).Days[1].Episodes.Single();

        Assert.True(episode.IsCataloged);
        Assert.Equal(CatalogStatus.Watching, episode.CatalogStatus);
        Assert.Equal("anime/42", episode.Href);
        Assert.False(episode.IsExternalHref);

        // Episode 5 airing means 4 have aired; 3 watched leaves 1 outstanding.
        Assert.Equal("1 episode behind", episode.CatalogNote);
        Assert.True(episode.IsBehind);
    }

    [Fact]
    public void UncatalogedTitlesLinkOutToAniList()
    {
        var load = Load(Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));

        var episode = Build(load).Days[1].Episodes.Single();

        Assert.False(episode.IsCataloged);
        Assert.Equal("https://anilist.co/anime/1", episode.Href);
        Assert.True(episode.IsExternalHref);
        Assert.Null(episode.CatalogNote);
    }

    // A private or unconfigured catalog must still render the AniList half.
    [Fact]
    public void AnEmptyOverlayYieldsNoBadgesAndDoesNotThrow()
    {
        var load = Load(Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)));

        var week = Build(load, CatalogOverlay.Empty(CatalogAccessState.Private));

        var episode = week.Days[1].Episodes.Single();
        Assert.False(episode.IsCataloged);
        Assert.Null(episode.CatalogStatus);
    }

    [Fact]
    public void AdultTitlesAreHiddenByDefault()
    {
        var adult = Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        adult.Media!.IsAdult = true;

        var load = Load(adult, Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero)));

        Assert.Equal(1, Build(load).VisibleEpisodeCount);
        Assert.Equal(2, Build(load, filters: new AiringWeekFilters { HideAdult = false }).VisibleEpisodeCount);
    }

    [Fact]
    public void ShortsCanBeHiddenWithoutTouchingTheQuery()
    {
        var shortEntry = Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        shortEntry.Media!.Format = "TV_SHORT";

        var load = Load(shortEntry, Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero)));

        Assert.Equal(1, Build(load, filters: new AiringWeekFilters { HideShorts = true }).VisibleEpisodeCount);
        Assert.Equal(2, Build(load).VisibleEpisodeCount);
    }

    [Fact]
    public void FormatAndCountryFiltersNarrowTheWeek()
    {
        var chinese = Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        chinese.Media!.CountryOfOrigin = "CN";
        chinese.Media.Format = "ONA";

        var load = Load(chinese, Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero)));

        Assert.Equal(1, Build(load, filters: new AiringWeekFilters { Country = "JP" }).VisibleEpisodeCount);
        Assert.Equal(1, Build(load, filters: new AiringWeekFilters { Format = "ONA" }).VisibleEpisodeCount);
    }

    [Fact]
    public void OnlyMineHidesEverythingElse()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 1, CatalogStatus.Watching, 0, null, 12));
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)),
            Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero)));

        var week = Build(load, overlay, new AiringWeekFilters { Catalog = CatalogHighlightFilter.OnlyMine });

        Assert.Equal(1, week.VisibleEpisodeCount);
        Assert.Equal(2, week.LoadedEpisodeCount);
    }

    // Dim-others is a visual treatment, not a filter - the rows have to stay.
    [Fact]
    public void DimOthersKeepsEveryRow()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 1, CatalogStatus.Watching, 0, null, 12));
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero)),
            Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero)));

        var week = Build(load, overlay, new AiringWeekFilters { Catalog = CatalogHighlightFilter.DimOthers });

        Assert.Equal(2, week.VisibleEpisodeCount);
    }

    [Fact]
    public void SearchNarrowsByTitle()
    {
        var load = Load(
            Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero), title: "Frieren"),
            Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero), title: "One Piece"));

        Assert.Equal(1, Build(load, filters: new AiringWeekFilters { Query = "frier" }).VisibleEpisodeCount);
    }

    [Fact]
    public void AvailableFormatsComeFromWhatLoaded_NotFromAFixedList()
    {
        var ona = Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        ona.Media!.Format = "ONA";

        var week = Build(Load(ona, Schedule(2, new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero))));

        Assert.Equal(["ONA", "TV"], week.AvailableFormats);
    }

    [Fact]
    public void DegradedStateIsCarriedThroughToTheViewModel()
    {
        var load = new AiringScheduleLoad
        {
            Schedules = [Schedule(1, new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero))],
            PagesLoaded = 3,
            IsComplete = false,
            WasTruncated = true,
            CompleteThrough = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
            DegradedMessage = "AniList stopped answering."
        };

        var week = Build(load);

        Assert.False(week.IsComplete);
        Assert.True(week.WasTruncated);
        Assert.Equal("AniList stopped answering.", week.DegradedMessage);
        Assert.NotNull(week.CompleteThrough);
    }

    [Fact]
    public void ArchiveEntriesCarryTheOwnersScoreAndTheCommunityScoreSeparately()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 1, CatalogStatus.Completed, 12, 9m, 12));

        var archive = new CalendarService().BuildArchive(
            2011,
            "SPRING",
            [new AniListMedia { Id = 1, Title = new AniListTitle { Romaji = "A" }, AverageScore = 82, Episodes = 12 }],
            overlay,
            CatalogHighlightFilter.All);

        var entry = Assert.Single(archive.Entries);

        Assert.Equal(9m, entry.OwnerScore);
        Assert.Equal(82, entry.CommunityScore);
        Assert.Equal("AniList 82", entry.CommunityScoreLabel);
        Assert.Equal(100, entry.ProgressPercent);
        Assert.Equal("anime/42", entry.Href);
        Assert.Equal("Spring 2011", archive.Heading);
    }

    [Fact]
    public void ArchiveDeDuplicatesRepeatedIdsAcrossPages()
    {
        var archive = new CalendarService().BuildArchive(
            2011,
            "SPRING",
            [
                new AniListMedia { Id = 1, Title = new AniListTitle { Romaji = "A" } },
                new AniListMedia { Id = 1, Title = new AniListTitle { Romaji = "A" } }
            ],
            CatalogOverlay.Empty(),
            CatalogHighlightFilter.All);

        Assert.Single(archive.Entries);
    }

    [Fact]
    public void ArchiveOnlyMineFiltersButStillReportsWhatLoaded()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 1, CatalogStatus.Completed, 12, null, 12));

        var archive = new CalendarService().BuildArchive(
            2011,
            "SPRING",
            [
                new AniListMedia { Id = 1, Title = new AniListTitle { Romaji = "Mine" } },
                new AniListMedia { Id = 2, Title = new AniListTitle { Romaji = "Not mine" } }
            ],
            overlay,
            CatalogHighlightFilter.OnlyMine);

        Assert.Single(archive.Entries);
        Assert.Equal(2, archive.LoadedCount);
        Assert.Equal(1, archive.CatalogedCount);
    }

    [Fact]
    public void ATitleWithNoNamesAtAllStillRendersSomething()
    {
        var archive = new CalendarService().BuildArchive(
            2011,
            "SPRING",
            [new AniListMedia { Id = 77 }],
            CatalogOverlay.Empty(),
            CatalogHighlightFilter.All);

        Assert.Equal("AniList #77", Assert.Single(archive.Entries).Title);
    }

    [Fact]
    public void AWholeYearBrowseBandsEntriesBySeason()
    {
        var archive = new CalendarService().BuildArchive(
            2011,
            AnimeSeasonCalendar.WholeYear,
            [
                Media(1, "FALL"),
                Media(2, "WINTER"),
                Media(3, "WINTER"),
                Media(4, null)
            ],
            CatalogOverlay.Empty(),
            CatalogHighlightFilter.All);

        // Broadcast order, unseasoned last rather than dropped.
        Assert.Equal(["WINTER", "FALL", "UNKNOWN"], archive.Groups.Select(group => group.Season!).ToArray());
        Assert.Equal(["Winter", "Fall", "Unknown"], archive.Groups.Select(group => group.Heading!).ToArray());
        Assert.Equal(2, archive.Groups[0].Entries.Count);
        Assert.True(archive.IsWholeYear);

        // Titled by the year alone: the bands name the seasons.
        Assert.Equal("2011", archive.Heading);

        // Nothing is lost to the grouping.
        Assert.Equal(4, archive.Entries.Count);
    }

    // The order AniList's sort produced has to survive inside each band.
    [Fact]
    public void BandingPreservesTheOrderWithinASeason()
    {
        var archive = new CalendarService().BuildArchive(
            2011,
            AnimeSeasonCalendar.WholeYear,
            [Media(9, "SPRING"), Media(3, "SPRING"), Media(7, "SPRING")],
            CatalogOverlay.Empty(),
            CatalogHighlightFilter.All);

        Assert.Equal([9, 3, 7], Assert.Single(archive.Groups).Entries.Select(entry => entry.AniListId).ToArray());
    }

    [Fact]
    public void ASingleSeasonBrowseProducesOneUnlabelledBand()
    {
        var archive = new CalendarService().BuildArchive(
            2011,
            "SPRING",
            [Media(1, "SPRING"), Media(2, "SPRING")],
            CatalogOverlay.Empty(),
            CatalogHighlightFilter.All);

        var group = Assert.Single(archive.Groups);

        Assert.Null(group.Season);
        Assert.Null(group.Heading);
        Assert.Equal(2, group.Entries.Count);
        Assert.False(archive.IsWholeYear);
        Assert.Equal("Spring 2011", archive.Heading);
    }

    // The catalog filter runs before banding, so an empty season simply has no band.
    [Fact]
    public void OnlyMineBandsOnlyTheSeasonsThatSurvive()
    {
        var overlay = Overlay(new CatalogOverlayItem(42, 2, CatalogStatus.Completed, 12, null, 12));

        var archive = new CalendarService().BuildArchive(
            2011,
            AnimeSeasonCalendar.WholeYear,
            [Media(1, "WINTER"), Media(2, "SUMMER")],
            overlay,
            CatalogHighlightFilter.OnlyMine);

        Assert.Equal(["SUMMER"], archive.Groups.Select(group => group.Season!).ToArray());
        Assert.Equal(2, archive.LoadedCount);
    }

    private static AniListMedia Media(int id, string? season) => new()
    {
        Id = id,
        Title = new AniListTitle { Romaji = $"Title {id}" },
        Season = season,
        SeasonYear = 2011
    };

    private static AiringWeekViewModel Build(
        AiringScheduleLoad load,
        CatalogOverlay? overlay = null,
        AiringWeekFilters? filters = null) =>
        new CalendarService().BuildAiringWeek(
            Week,
            Zone,
            Now,
            load,
            overlay ?? CatalogOverlay.Empty(),
            filters ?? new AiringWeekFilters());

    private static AiringScheduleLoad Load(params AniListAiringSchedule[] schedules) => new()
    {
        Schedules = schedules,
        PagesLoaded = 1,
        IsComplete = true
    };

    private static CatalogOverlay Overlay(params CatalogOverlayItem[] items) =>
        new(items.ToDictionary(item => item.AniListId), CatalogAccessState.Available);

    private static AniListAiringSchedule Schedule(
        int id,
        DateTimeOffset airsAtUtc,
        int episode = 1,
        string title = "Title") => new()
    {
        Id = id,
        MediaId = id,
        Episode = episode,
        AiringAt = airsAtUtc.ToUnixTimeSeconds(),
        Media = new AniListMedia
        {
            Id = id,
            Title = new AniListTitle { Romaji = title },
            Format = "TV",
            CountryOfOrigin = "JP",
            SiteUrl = $"https://anilist.co/anime/{id}"
        }
    };
}
