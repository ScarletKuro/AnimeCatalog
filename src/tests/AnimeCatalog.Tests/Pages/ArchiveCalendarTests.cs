using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Pages;

public sealed class ArchiveCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    // This test IS the requirement: the year picker reaches back to 1940, is a plain select rather
    // than a strip to scroll, and lists newest first.
    [Fact]
    public void TheYearOptionsSpanTheWholeAniListRange_NotJust2009Onwards()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            var years = cut.FindAll(".filters-card select")[0]
                .QuerySelectorAll("option")
                .Select(option => int.Parse(option.GetAttribute("value")!))
                .ToList();

            Assert.Equal(2028, years[0]);
            Assert.Equal(AnimeSeasonCalendar.MinimumYear, years[^1]);
            Assert.Equal(1940, years[^1]);

            // The two years AniChart cannot reach.
            Assert.Contains(1963, years);
            Assert.Contains(1990, years);

            // Newest first, so the common case is the first thing in the list.
            Assert.Equal(years.OrderByDescending(year => year).ToList(), years);
        });
    }

    [Fact]
    public void WithNoRouteParametersItLandsOnTheCurrentSeason()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() => Assert.Contains("Summer 2026", cut.Find(".panel__header h2").TextContent));
    }

    [Fact]
    public void RouteParametersDriveTheHeading()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 1963)
            .Add(p => p.Season, "winter"));

        cut.WaitForAssertion(() => Assert.Contains("Winter 1963", cut.Find(".panel__header h2").TextContent));
    }

    [Fact]
    public void AnUnknownSeasonSlugFallsBackToTheCurrentSeason()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "autumn"));

        cut.WaitForAssertion(() => Assert.Contains("Summer 2011", cut.Find(".panel__header h2").TextContent));
    }

    // A year past the ceiling would ask AniList for a season that cannot exist.
    [Fact]
    public void AYearOutsideTheRangeIsClamped()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 1800)
            .Add(p => p.Season, "spring"));

        cut.WaitForAssertion(() => Assert.Contains("Spring 1940", cut.Find(".panel__header h2").TextContent));
    }

    [Fact]
    public void TheSeasonPickerOffersFourSeasonsPlusTheWholeYear()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "spring"));

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll(".season-picker__option");
            Assert.Equal(5, links.Count);
            Assert.All(links, link => Assert.Contains("calendar/archive/2011/", link.GetAttribute("href")));
        });
    }

    [Fact]
    public void ItPagesUntilAniListRunsOutAndRendersEveryEntry()
    {
        var browse = new StubBrowseService
        {
            Pages = [Media(1, 50), Media(51, 20)]
        };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() => Assert.Equal(70, cut.FindAll(".poster-card").Count));
        Assert.Equal(2, browse.CallCount);
    }

    [Fact]
    public void CatalogedEntriesAreRingedAndLinkInward()
    {
        var overlay = new CatalogOverlay(
            new Dictionary<int, CatalogOverlayItem> { [1] = new(42, 1, CatalogStatus.Completed, 12, 9m, 12) },
            CatalogAccessState.Available);

        var browse = new StubBrowseService { Pages = [Media(1, 2)] };

        using var context = Create(browse, overlay);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            var highlighted = Assert.Single(cut.FindAll(".poster-card--highlighted"));
            Assert.EndsWith("anime/42", highlighted.QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void UncatalogedEntriesLinkOutToAniList()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 1)] };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".poster-card--highlighted"));

            var link = cut.Find(".poster-card a");
            Assert.Equal("_blank", link.GetAttribute("target"));
            Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        });
    }

    // The community score belongs in the footer; PosterCard.Score means the owner's own rating.
    [Fact]
    public void TheCommunityScoreIsAFooterFactRatherThanTheScoreChip()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 1)] };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AniList 82", cut.Find(".poster-card__footer").TextContent);

            // Nothing is in the catalog, so there is no owner score to show.
            Assert.Empty(cut.FindAll(".poster-card__score"));
        });
    }

    [Fact]
    public void APrivateCatalogKeepsTheGridAndDropsTheCatalogFilter()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 3)] };

        using var context = Create(browse, CatalogOverlay.Empty(CatalogAccessState.Private));
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".poster-card").Count);
            Assert.Empty(cut.FindAll(".access-card"));
            Assert.Single(cut.FindAll(".panel__notice"));
            Assert.DoesNotContain("Only mine", cut.Markup);
        });
    }

    [Fact]
    public void AnUnconfiguredCatalogAddsNoNotice()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 2)] };

        using var context = Create(browse, CatalogOverlay.Empty(CatalogAccessState.NotConfigured));
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".poster-card").Count);
            Assert.Empty(cut.FindAll(".panel__notice"));
        });
    }

    [Fact]
    public void AnEmptySeasonSaysSoRatherThanLookingBroken()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 1941)
            .Add(p => p.Season, "winter"));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".empty-card"));
            Assert.Contains("1941", cut.Find(".empty-card").TextContent);
        });
    }

    [Fact]
    public void WhenAniListIsDownWithNothingLoadedItShowsTheUnavailableCard()
    {
        using var context = Create(new StubBrowseService
        {
            Failure = new AniListUnavailableException(403, "temporarily disabled")
        });

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AniList is unavailable", cut.Find(".error-card").TextContent);
            Assert.Empty(cut.FindAll(".poster-grid"));
        });
    }

    // A failure after the first page keeps what arrived rather than throwing the season away.
    [Fact]
    public void AFailureAfterTheFirstPageKeepsWhatLoaded()
    {
        var browse = new StubBrowseService
        {
            Pages = [Media(1, 50)],
            AlwaysClaimAnotherPage = true,
            FailOnPage = 2
        };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(50, cut.FindAll(".poster-card").Count);
            Assert.Empty(cut.FindAll(".error-card"));
            Assert.Single(cut.FindAll(".panel__notice"));
        });
    }

    // Signing in changes which tiles are ringed, not which titles the season holds.
    [Fact]
    public void RefreshingTheOverlayDoesNotRefetchTheSeason()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 3)] };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".poster-card").Count));
        var afterFirstLoad = browse.CallCount;

        context.Services.GetRequiredService<StubAuthStateNotifier>().SignInAs("owner", isAdmin: true);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".poster-card").Count));
        Assert.Equal(afterFirstLoad, browse.CallCount);
    }

    [Fact]
    public void TheYearStepButtonsStopAtTheEndsOfTheRange()
    {
        using var context = Create(new StubBrowseService());

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, AnimeSeasonCalendar.MinimumYear)
            .Add(p => p.Season, "winter"));

        cut.WaitForAssertion(() =>
        {
            var back = cut.FindAll(".panel__header button")[0];
            Assert.True(back.HasAttribute("disabled"));
        });
    }

    // Regression: value="@_hideAdult" rendered "True" while the options are lowercase "true", so no
    // option matched and the browser fell back to displaying the first one. The control then lied -
    // it read "Hidden" whatever the field actually held, and picking "Shown" snapped the label back
    // while adult titles really were being shown.
    [Fact]
    public void TheAdultFilterSelectsHiddenByDefault()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            var select = AdultSelect(cut);

            Assert.Equal("true", select.GetAttribute("value"));
            Assert.Equal("Hidden", select.QuerySelectorAll("option")
                .Single(option => option.GetAttribute("value") == select.GetAttribute("value"))
                .TextContent.Trim());
        });
    }

    [Fact]
    public void HidingAdultTitlesIsAskedOfAniListRatherThanFilteredLocally()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 1)] };
        using var context = Create(browse);

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() => Assert.NotNull(browse.LastRequest));
        Assert.False(browse.LastRequest!.IsAdult);
    }

    [Fact]
    public void ShowingAdultTitlesSticks_AndTheSelectSaysSo()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 1)] };
        using var context = Create(browse);

        var cut = context.Render<ArchiveCalendar>();
        cut.WaitForAssertion(() => Assert.NotNull(browse.LastRequest));

        AdultSelect(cut).Change("false");

        cut.WaitForAssertion(() =>
        {
            // The label has to follow the field, which is exactly what the casing bug broke.
            Assert.Equal("false", AdultSelect(cut).GetAttribute("value"));
            Assert.Null(browse.LastRequest!.IsAdult);
        });
    }

    // Regression: the season picker marks itself active off the URL, so a bare /calendar/archive left
    // the heading reading "Summer 2026" with nothing highlighted underneath it.
    [Fact]
    public void TheBareRouteResolvesItselfIntoTheAddressBar()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
            Assert.EndsWith("calendar/archive/2026/summer", navigation.Uri));
    }

    [Fact]
    public void TheBareRouteStillHighlightsTheSeasonItLandedOn()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });

        var cut = context.Render<ArchiveCalendar>();

        cut.WaitForAssertion(() =>
        {
            var active = Assert.Single(cut.FindAll(".season-picker__option.active"));
            Assert.Equal("Summer", active.TextContent.Trim());
        });
    }

    // A season this page had to correct must also end up in the address, or the picker disagrees with
    // the heading again.
    [Fact]
    public void ACorrectedSeasonIsWrittenBackToTheAddressBar()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "autumn"));

        cut.WaitForAssertion(() => Assert.EndsWith("calendar/archive/2011/summer", navigation.Uri));
    }

    private static AngleSharp.Dom.IElement AdultSelect(IRenderedComponent<ArchiveCalendar> cut) =>
        cut.FindAll(".filters-card label")
            .Single(label => label.QuerySelector(".field-label")!.TextContent.Trim() == "Adult titles")
            .QuerySelector("select")!;

    // "Whole year" is the archive's own idea, not a MediaSeason. It must reach AniList as an absent
    // argument, because that is what makes the season filter disappear entirely.
    [Fact]
    public void WholeYearAsksAniListForTheYearWithNoSeasonFilter()
    {
        var browse = new StubBrowseService { Pages = [Media(1, 3)] };
        using var context = Create(browse);

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "all"));

        cut.WaitForAssertion(() => Assert.NotNull(browse.LastRequest));
        Assert.Null(browse.LastRequest!.Season);
        Assert.Equal(2011, browse.LastRequest.SeasonYear);
    }

    [Fact]
    public void WholeYearIsTitledByTheYearAlone()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "all"));

        cut.WaitForAssertion(() => Assert.Equal("2011", cut.Find(".panel__header h2").TextContent.Trim()));
    }

    [Fact]
    public void WholeYearBandsTheResultsBySeasonInBroadcastOrder()
    {
        var browse = new StubBrowseService
        {
            Pages =
            [
                [
                    Title(1, "FALL"),
                    Title(2, "WINTER"),
                    Title(3, "SUMMER"),
                    Title(4, "SPRING"),
                    Title(5, null)
                ]
            ]
        };

        using var context = Create(browse);
        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "all"));

        cut.WaitForAssertion(() =>
        {
            var bands = cut.FindAll(".archive-band__title").Select(band => band.TextContent.Trim()).ToArray();

            // Broadcast order, with the title AniList left unseasoned last rather than dropped.
            Assert.Equal(["Winter", "Spring", "Summer", "Fall", "Unknown"], bands);
            Assert.Equal(5, cut.FindAll(".poster-card").Count);
        });
    }

    // A single season renders one band and must not grow a redundant heading above the grid - the
    // panel heading already says which season it is.
    [Fact]
    public void ASingleSeasonRendersOneUnlabelledBand()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 3)] });

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "spring"));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".archive-band"));
            Assert.Empty(cut.FindAll(".archive-band__header"));
            Assert.Equal(3, cut.FindAll(".poster-card").Count);
        });
    }

    [Fact]
    public void ThePickerOffersTheWholeYearAlongsideTheFourSeasons()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });

        // Navigated rather than only parameterised: NavLink resolves .active against the current URL,
        // and rendering a component directly leaves that at the base address.
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("calendar/archive/2011/all");

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "all"));

        cut.WaitForAssertion(() =>
        {
            var options = cut.FindAll(".season-picker__option").Select(o => o.TextContent.Trim()).ToArray();
            Assert.Equal(["Winter", "Spring", "Summer", "Fall", "Whole year"], options);

            var active = Assert.Single(cut.FindAll(".season-picker__option.active"));
            Assert.Equal("Whole year", active.TextContent.Trim());
        });
    }

    // Stepping the year from a whole-year view has to stay whole-year rather than snapping to Winter.
    [Fact]
    public void SteppingTheYearKeepsTheWholeYearView()
    {
        using var context = Create(new StubBrowseService { Pages = [Media(1, 1)] });
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = context.Render<ArchiveCalendar>(parameters => parameters
            .Add(p => p.Year, 2011)
            .Add(p => p.Season, "all"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".archive-band")));

        cut.FindAll(".panel__header button")[0].Click();

        cut.WaitForAssertion(() => Assert.EndsWith("calendar/archive/2010/all", navigation.Uri));
    }

    private static AniListMedia Title(int id, string? season) => new()
    {
        Id = id,
        Title = new AniListTitle { Romaji = $"Title {id}" },
        Format = "TV",
        Season = season,
        SeasonYear = 2011,
        AverageScore = 82,
        SiteUrl = $"https://anilist.co/anime/{id}"
    };

    private static BunitContext Create(StubBrowseService browse, CatalogOverlay? overlay = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var notifier = new StubAuthStateNotifier();

        context.Services.AddSingleton<IAniListBrowseService>(browse);
        context.Services.AddSingleton<ICatalogService>(new StubCatalogService(overlay ?? CatalogOverlay.Empty()));
        context.Services.AddSingleton(new CalendarService());
        context.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now, TimeZoneInfo.Utc));
        context.Services.AddSingleton(notifier);
        context.Services.AddSingleton<IAuthStateNotifier>(notifier);

        return context;
    }

    private static List<AniListMedia> Media(int firstId, int count) =>
        Enumerable.Range(firstId, count)
            .Select(id => new AniListMedia
            {
                Id = id,
                Title = new AniListTitle { Romaji = $"Title {id}" },
                Format = "TV",
                SeasonYear = 2011,
                Episodes = 12,
                AverageScore = 82,
                SiteUrl = $"https://anilist.co/anime/{id}"
            })
            .ToList();

    private sealed class StubBrowseService : IAniListBrowseService
    {
        public List<List<AniListMedia>> Pages { get; init; } = [];

        public bool AlwaysClaimAnotherPage { get; init; }

        public int? FailOnPage { get; init; }

        public Exception? Failure { get; init; }

        public int CallCount { get; private set; }

        /// <summary>The last request handed to this stub, so the page-to-query mapping is assertable.</summary>
        public AniListBrowseRequest? LastRequest { get; private set; }

        public Task<AniListPageResult<AniListMedia>> GetBrowsePageAsync(
            AniListBrowseRequest request,
            int page,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;

            if (Failure is not null)
            {
                throw Failure;
            }

            if (FailOnPage == page)
            {
                throw new AniListUnavailableException(403, "temporarily disabled");
            }

            var items = page <= Pages.Count ? Pages[page - 1] : [];
            var hasNext = AlwaysClaimAnotherPage || page < Pages.Count;

            return Task.FromResult(new AniListPageResult<AniListMedia>(items, page, items.Count > 0 && hasNext));
        }

        public Task<AiringScheduleLoad> GetAiringSchedulesAsync(
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            IProgress<AiringScheduleLoad>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The archive view does not read the schedule.");
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly CatalogOverlay _overlay;

        public StubCatalogService(CatalogOverlay overlay) => _overlay = overlay;

        public bool IsConfigured => _overlay.State != CatalogAccessState.NotConfigured;

        public Task<CatalogOverlay> GetCatalogOverlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_overlay);

        public void InvalidateCatalogOverlay()
        {
        }

        public Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AnimeCatalog.Models.Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
