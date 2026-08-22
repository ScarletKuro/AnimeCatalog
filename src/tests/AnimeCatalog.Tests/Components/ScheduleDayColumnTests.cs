using AnimeCatalog.Components;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ScheduleDayColumnTests
{
    private static readonly DateOnly Monday = new(2026, 8, 24);

    [Fact]
    public void ItNamesTheDayAndTheDate()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(3));

        Assert.Equal("Monday", cut.Find(".schedule-day__name").TextContent.Trim());
        Assert.Equal("24 Aug", cut.Find(".schedule-day__date").TextContent.Trim());
        Assert.Equal("2026-08-24", cut.Find(".schedule-day__date").GetAttribute("datetime"));
    }

    // A bare <section> is not a named region without this, and the names are what make the seven day
    // columns navigable rather than one undifferentiated blob.
    [Fact]
    public void TheSectionIsLabelledByItsOwnHeading()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(1));

        var section = cut.Find("section.schedule-day");
        var headingId = cut.Find(".schedule-day__name").GetAttribute("id");

        Assert.Equal(headingId, section.GetAttribute("aria-labelledby"));
        Assert.False(string.IsNullOrWhiteSpace(headingId));
    }

    // The day name must stay an h3 so episode titles can be h4 without skipping a level.
    [Fact]
    public void TheDayNameIsAnH3()
    {
        using var context = new BunitContext();

        Assert.Equal("H3", Render(context, Episodes(1)).Find(".schedule-day__name").TagName);
    }

    [Fact]
    public void MoreThanThePreviewCount_ShowsADisclosureAndOnlyTheFirstTwelve()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(30));

        Assert.Equal(12, cut.FindAll(".schedule-episode").Count);
        Assert.Equal("Show all 30", cut.Find("button.disclosure span").TextContent.Trim());
    }

    // The honesty contract: a collapsed column still reports the true number.
    [Fact]
    public void TheHeaderCountIsTheTrueTotal_NotWhatIsVisible()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(30));

        Assert.StartsWith("30", cut.Find(".schedule-day__count").TextContent.Trim());
    }

    [Fact]
    public void TheDisclosureRevealsTheRest_AndTheCountNeverChanges()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(30));

        cut.Find("button.disclosure").Click();

        Assert.Equal(30, cut.FindAll(".schedule-episode").Count);
        Assert.StartsWith("30", cut.Find(".schedule-day__count").TextContent.Trim());
        Assert.Equal("Show fewer", cut.Find("button.disclosure span").TextContent.Trim());
    }

    [Fact]
    public void AtOrBelowThePreviewCount_ThereIsNoDisclosure()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(12));

        Assert.Equal(12, cut.FindAll(".schedule-episode").Count);
        Assert.Empty(cut.FindAll("button.disclosure"));
    }

    [Fact]
    public void AnEmptyDayShowsTheEmptyNote_NotSkeletons()
    {
        using var context = new BunitContext();

        var cut = Render(context, []);

        Assert.Single(cut.FindAll(".empty-note"));
        Assert.Empty(cut.FindAll(".schedule-episode--skeleton"));
        Assert.Contains("schedule-day--empty", cut.Find("section.schedule-day").ClassList);
    }

    // While the week is still paging in, "nothing airs" is a claim the page cannot yet make.
    [Fact]
    public void AnEmptyDayStillLoadingShowsSkeletons_NotTheEmptyNote()
    {
        using var context = new BunitContext();

        var cut = Render(context, [], isLoading: true);

        Assert.NotEmpty(cut.FindAll(".schedule-episode--skeleton"));
        Assert.Empty(cut.FindAll(".empty-note"));

        // Nor should it fade out before it has an answer.
        Assert.DoesNotContain("schedule-day--empty", cut.Find("section.schedule-day").ClassList);
    }

    [Fact]
    public void TodayIsMarkedByAriaCurrentAndAVisibleChip()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(1), isToday: true);

        Assert.Equal("date", cut.Find("section.schedule-day").GetAttribute("aria-current"));
        Assert.Contains("schedule-day--today", cut.Find("section.schedule-day").ClassList);
        Assert.Equal("Today", cut.Find(".chip--live").TextContent.Trim());
    }

    [Fact]
    public void AnOrdinaryDayCarriesNoneOfThat()
    {
        using var context = new BunitContext();

        var cut = Render(context, Episodes(1));

        Assert.Null(cut.Find("section.schedule-day").GetAttribute("aria-current"));
        Assert.Empty(cut.FindAll(".chip--live"));
    }

    // Time order is meaningful, so the list has to be ordered rather than unordered.
    [Fact]
    public void TheEpisodesAreAnOrderedList()
    {
        using var context = new BunitContext();

        Assert.Equal("OL", Render(context, Episodes(2)).Find(".schedule-day__items").TagName);
    }

    [Fact]
    public void DimUncataloged_PlaysDownOnlyTheRowsThatAreNotInTheCatalog()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleDayColumn>(parameters => parameters
            .Add(p => p.Date, Monday)
            .Add(p => p.Episodes, [
                Episode(1),
                Episode(2) with { Catalog = new CatalogOverlayItem(9, 2, AnimeCatalog.Models.CatalogStatus.Watching, 1, null, 12) }
            ])
            .Add(p => p.DimUncataloged, true));

        Assert.Single(cut.FindAll(".schedule-episode--dimmed"));
        Assert.Single(cut.FindAll(".schedule-episode--cataloged"));
    }

    private static IRenderedComponent<ScheduleDayColumn> Render(
        BunitContext context,
        IReadOnlyList<ScheduleEpisodeViewModel> episodes,
        bool isToday = false,
        bool isLoading = false) =>
        context.Render<ScheduleDayColumn>(parameters => parameters
            .Add(p => p.Date, Monday)
            .Add(p => p.Episodes, episodes)
            .Add(p => p.IsToday, isToday)
            .Add(p => p.IsLoading, isLoading));

    private static List<ScheduleEpisodeViewModel> Episodes(int count) =>
        Enumerable.Range(1, count).Select(Episode).ToList();

    private static ScheduleEpisodeViewModel Episode(int index) => new()
    {
        AniListId = index,
        Title = $"Title {index}",
        AirsAtLocal = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero).AddMinutes(index * 30),
        Episode = index,
        Format = "TV"
    };
}
