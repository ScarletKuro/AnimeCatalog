using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Pages;

public sealed class AiringCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    // The anti-reflow contract, and the single most important assertion on this page: the columns
    // come from the week, not from the data, so all seven exist before any episode arrives.
    [Fact]
    public void TheSevenDayColumnsExistBeforeAnyEpisodeArrives()
    {
        using var context = Create(new StubBrowseService { HangForever = true });

        var cut = context.Render<AiringCalendar>();

        Assert.Equal(7, cut.FindAll(".schedule-day").Count);
    }

    // Not a LoadingIndicator: one state machine, and the columns have to be present from frame zero.
    [Fact]
    public void WhileLoadingItShowsSkeletonRowsRatherThanALoadingCard()
    {
        using var context = Create(new StubBrowseService { HangForever = true });

        var cut = context.Render<AiringCalendar>();

        Assert.Empty(cut.FindAll(".loading-card"));
        Assert.NotEmpty(cut.FindAll(".schedule-episode--skeleton"));
    }

    [Fact]
    public void EpisodesLandInTheirDayColumn()
    {
        var browse = new StubBrowseService
        {
            Load = LoadOf(
                Schedule(1, new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero)),
                Schedule(2, new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero)))
        };

        using var context = Create(browse);
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".schedule-episode").Count));

        var columns = cut.FindAll(".schedule-day");
        Assert.Single(columns[0].QuerySelectorAll(".schedule-episode"));
        Assert.Single(columns[2].QuerySelectorAll(".schedule-episode"));
        Assert.Empty(columns[1].QuerySelectorAll(".schedule-episode"));
    }

    [Fact]
    public void TodayIsMarkedOnExactlyOneColumn()
    {
        using var context = Create(new StubBrowseService { Load = AiringScheduleLoad.Empty });

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            var today = Assert.Single(cut.FindAll(".schedule-day--today"));
            Assert.Contains("Today", today.TextContent);
            Assert.Equal("date", today.GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void TheStatusLineIsTheOnlyLiveRegion()
    {
        using var context = Create(new StubBrowseService { Load = AiringScheduleLoad.Empty });

        var cut = context.Render<AiringCalendar>();

        // Three hundred insertions into a live region would be a screen-reader denial of service, so
        // the grid must never be one.
        Assert.Single(cut.FindAll("[aria-live]"));
        Assert.Contains("schedule-toolbar__status", cut.Find("[aria-live]").ClassList);
    }

    [Fact]
    public void WhenAniListIsDownWithNothingLoadedItShowsTheUnavailableCard()
    {
        using var context = Create(new StubBrowseService
        {
            Failure = new AniListUnavailableException(403, "The AniList API has been temporarily disabled.")
        });

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".error-card"));
            Assert.Contains("AniList is unavailable", cut.Find(".error-card").TextContent);

            // AniList's own words appear as a secondary line, never as the primary message.
            Assert.Contains("temporarily disabled", cut.Markup);
        });
    }

    [Fact]
    public void ARealErrorIsDistinguishedFromAnOutage()
    {
        using var context = Create(new StubBrowseService { Failure = new InvalidOperationException("bad query") });

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The schedule failed to load", cut.Find(".error-card").TextContent);
            Assert.Contains("bad query", cut.Find(".error-card").TextContent);
        });
    }

    // Half a week on screen beats an error card, so a partial result keeps the grid and annotates it.
    [Fact]
    public void APartialWeekKeepsTheGridAndExplainsItself()
    {
        var browse = new StubBrowseService
        {
            Load = LoadOf(Schedule(1, new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero))) with
            {
                IsComplete = false,
                DegradedMessage = "AniList stopped answering."
            }
        };

        using var context = Create(browse);
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".error-card"));
            Assert.Single(cut.FindAll(".panel__notice"));
            Assert.Equal(7, cut.FindAll(".schedule-day").Count);
        });
    }

    [Fact]
    public void ATruncatedWeekNamesTheBoundaryRatherThanJustSayingIncomplete()
    {
        var browse = new StubBrowseService
        {
            Load = LoadOf(Schedule(1, new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero))) with
            {
                WasTruncated = true,
                CompleteThrough = new DateTimeOffset(2026, 8, 20, 15, 0, 0, TimeSpan.Zero)
            }
        };

        using var context = Create(browse);
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            var notice = cut.Find(".panel__notice").TextContent;
            Assert.Contains("complete through", notice);
            Assert.Contains("Thursday", notice);
        });
    }

    // The key divergence from /catalog: a private catalog must not take the page over.
    [Fact]
    public void APrivateCatalogStillRendersTheWholeSchedule()
    {
        var browse = new StubBrowseService
        {
            Load = LoadOf(Schedule(1, new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)))
        };

        using var context = Create(browse, CatalogOverlay.Empty(CatalogAccessState.Private));
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(7, cut.FindAll(".schedule-day").Count);
            Assert.Single(cut.FindAll(".schedule-episode"));

            // PrivateCatalogState renders .access-card and would replace the grid entirely.
            Assert.Empty(cut.FindAll(".access-card"));

            // The missing highlighting is explained exactly once.
            Assert.Single(cut.FindAll(".panel__notice"));
        });
    }

    [Fact]
    public void APrivateCatalogDropsTheCatalogFilterRatherThanDisablingIt()
    {
        using var context = Create(
            new StubBrowseService { Load = AiringScheduleLoad.Empty },
            CatalogOverlay.Empty(CatalogAccessState.Private));

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Only mine", cut.Markup));
    }

    // An unconfigured Supabase is not a fault, so a self-hosted instance must not be nagged.
    [Fact]
    public void AnUnconfiguredCatalogRendersTheGridWithNoNoticeAtAll()
    {
        using var context = Create(
            new StubBrowseService { Load = AiringScheduleLoad.Empty },
            CatalogOverlay.Empty(CatalogAccessState.NotConfigured));

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(7, cut.FindAll(".schedule-day").Count);
            Assert.Empty(cut.FindAll(".panel__notice"));
        });
    }

    [Fact]
    public void CatalogedTitlesCarryTheirBadgeProgressAndAnInternalLink()
    {
        var overlay = new CatalogOverlay(
            new Dictionary<int, CatalogOverlayItem>
            {
                [1] = new(42, 1, CatalogStatus.Watching, 3, 8m, 12)
            },
            CatalogAccessState.Available);

        var browse = new StubBrowseService
        {
            Load = LoadOf(Schedule(1, new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), episode: 5))
        };

        using var context = Create(browse, overlay);
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".schedule-episode--cataloged"));
            Assert.Contains("1 episode behind", cut.Markup);
            Assert.EndsWith("anime/42", cut.Find("a.schedule-episode__link").GetAttribute("href"));
        });
    }

    [Fact]
    public void WhileLoadingItOffersCancel_AndReloadOnceSettled()
    {
        using var context = Create(new StubBrowseService { HangForever = true });

        var cut = context.Render<AiringCalendar>();

        Assert.Contains("Cancel", cut.Markup);

        cut.Find("button.button--ghost:last-child").Click();

        cut.WaitForAssertion(() => Assert.Contains("Reload", cut.Markup));
    }

    [Fact]
    public void TheWeekRangeMovesWithThePreviousAndNextButtons()
    {
        using var context = Create(new StubBrowseService { Load = AiringScheduleLoad.Empty });

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() => Assert.Contains("17-23 August 2026", cut.Find(".schedule-toolbar__range").TextContent));

        cut.FindAll("button.button--ghost")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("10-16 August 2026", cut.Find(".schedule-toolbar__range").TextContent));
    }

    // Signing in changes the overlay, not the week. Re-spending five to seven AniList requests on a
    // week that is already on screen would be the easy mistake here.
    [Fact]
    public void RefreshingTheOverlayDoesNotRefetchTheWeek()
    {
        var browse = new StubBrowseService { Load = AiringScheduleLoad.Empty };
        using var context = Create(browse);

        var cut = context.Render<AiringCalendar>();
        cut.WaitForAssertion(() => Assert.Equal(1, browse.CallCount));

        var notifier = context.Services.GetRequiredService<StubAuthStateNotifier>();
        notifier.SignInAs("owner", isAdmin: true);

        cut.WaitForAssertion(() => Assert.Equal(1, browse.CallCount));
    }

    // Regression: value="@bool" renders "True"/"False" while the options are lowercase, so no option
    // matched and the browser fell back to displaying the first one regardless of the real state. The
    // adult and shorts controls both read as whatever sat at the top of their list.
    [Fact]
    public void TheBoolFiltersReportTheirActualState()
    {
        using var context = Create(new StubBrowseService { Load = AiringScheduleLoad.Empty });

        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", FilterSelect(cut, "Adult titles").GetAttribute("value"));
            Assert.Equal("false", FilterSelect(cut, "Shorts").GetAttribute("value"));
        });
    }

    [Fact]
    public void ShowingAdultTitlesIsReflectedBackInTheSelect()
    {
        var adult = Schedule(1, new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero));
        adult.Media!.IsAdult = true;

        using var context = Create(new StubBrowseService { Load = LoadOf(adult) });
        var cut = context.Render<AiringCalendar>();

        // Hidden by default, so the one adult entry does not render.
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".schedule-episode")));

        FilterSelect(cut, "Adult titles").Change("false");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", FilterSelect(cut, "Adult titles").GetAttribute("value"));
            Assert.Single(cut.FindAll(".schedule-episode"));
        });
    }

    [Fact]
    public void HidingShortsIsReflectedBackInTheSelect()
    {
        var shortEntry = Schedule(1, new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero));
        shortEntry.Media!.Format = "TV_SHORT";

        using var context = Create(new StubBrowseService { Load = LoadOf(shortEntry) });
        var cut = context.Render<AiringCalendar>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".schedule-episode")));

        FilterSelect(cut, "Shorts").Change("true");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", FilterSelect(cut, "Shorts").GetAttribute("value"));
            Assert.Empty(cut.FindAll(".schedule-episode"));
        });
    }

    private static AngleSharp.Dom.IElement FilterSelect(IRenderedComponent<AiringCalendar> cut, string label) =>
        cut.FindAll(".filters-card label")
            .Single(field => field.QuerySelector(".field-label")!.TextContent.Trim() == label)
            .QuerySelector("select")!;

    // The seven-column form only works because this page opts out of .main-content's 1320px measure.
    // Without it a title gets about 114px and truncates two words in, so the class is part of the
    // layout contract rather than decoration.
    [Fact]
    public void ThePageOptsOutOfTheDefaultContentWidth()
    {
        using var context = Create(new StubBrowseService { Load = AiringScheduleLoad.Empty });

        var cut = context.Render<AiringCalendar>();

        Assert.Contains("calendar-wide", cut.Find("section.stack").ClassList);
    }

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

    private static AiringScheduleLoad LoadOf(params AniListAiringSchedule[] schedules) => new()
    {
        Schedules = schedules,
        PagesLoaded = 1,
        IsComplete = true,
        CompleteThrough = schedules.Length == 0 ? null : schedules[^1].AiringAtUtc
    };

    private static AniListAiringSchedule Schedule(int id, DateTimeOffset airsAt, int episode = 1) => new()
    {
        Id = id,
        MediaId = id,
        Episode = episode,
        AiringAt = airsAt.ToUnixTimeSeconds(),
        Media = new AniListMedia
        {
            Id = id,
            Title = new AniListTitle { Romaji = $"Title {id}" },
            Format = "TV",
            CountryOfOrigin = "JP",
            SiteUrl = $"https://anilist.co/anime/{id}"
        }
    };

    private sealed class StubBrowseService : IAniListBrowseService
    {
        public AiringScheduleLoad Load { get; init; } = AiringScheduleLoad.Empty;

        public Exception? Failure { get; init; }

        /// <summary>Never completes, so the first-render state can be asserted.</summary>
        public bool HangForever { get; init; }

        public int CallCount { get; private set; }

        public async Task<AiringScheduleLoad> GetAiringSchedulesAsync(
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            IProgress<AiringScheduleLoad>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (HangForever)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            progress?.Report(Load);
            return Load;
        }

        public Task<AniListPageResult<AniListMedia>> GetBrowsePageAsync(
            AniListBrowseRequest request,
            int page,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The airing view does not browse.");
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
