using System.Globalization;
using AnimeCatalog.Components;
using AnimeCatalog.Models;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ScheduleEpisodeRowTests
{
    private static readonly DateTimeOffset Monday1830 = new(2026, 8, 24, 18, 30, 0, TimeSpan.FromHours(3));

    [Fact]
    public void ItLeadsWithTheClockAndTheEpisodeNumber()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Episode, 18));

        Assert.Equal("18:30", cut.Find(".schedule-episode__clock").TextContent.Trim());
        Assert.Equal("Ep 18", cut.Find(".schedule-episode__ep").TextContent.Trim());
        Assert.Equal("Frieren", cut.Find(".schedule-episode__title").TextContent.Trim());
    }

    // A schedule column is sized for "18:30". An en-US browser turning that into "6:30 PM" would
    // overflow it, which is why the formatter is invariant even for visible text.
    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void TheClockIsInvariant_SoABrowserLocaleCannotChangeIt(string culture)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            using var context = new BunitContext();

            var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
                .Add(p => p.AirsAtLocal, Monday1830)
                .Add(p => p.Title, "Frieren"));

            Assert.Equal("18:30", cut.Find(".schedule-episode__clock").TextContent.Trim());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void WithNoEpisodeNumber_TheEpisodeChipIsLeftOut()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(cut.FindAll(".schedule-episode__ep"));
    }

    [Fact]
    public void WithNoCover_ItFallsBackToThePosterInitial()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "frieren"));

        Assert.Empty(cut.FindAll(".schedule-episode__poster img"));
        Assert.Equal("F", cut.Find(".poster-fallback").TextContent.Trim());
    }

    // Unlike PosterCard, where the image IS the card, the title sits next to the cover here - so
    // naming it would make a fifty-row column announce every title twice.
    [Fact]
    public void TheCoverIsDecorative_SoItCarriesNoAltText()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.CoverUrl, "https://example.test/cover.jpg"));

        Assert.Equal(string.Empty, cut.Find(".schedule-episode__poster img").GetAttribute("alt"));
        Assert.Equal("lazy", cut.Find(".schedule-episode__poster img").GetAttribute("loading"));
    }

    [Fact]
    public void ACatalogStatus_RendersTheBadgeAndFlipsTheCatalogedTreatment()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.CatalogStatus, CatalogStatus.Watching)
            .Add(p => p.EpisodesWatched, 3)
            .Add(p => p.TotalEpisodes, 12));

        Assert.Contains("schedule-episode--cataloged", cut.Find("article").ClassList);
        Assert.Single(cut.FindAll(".status-badge"));
        Assert.Equal("3 / 12", cut.Find(".progress-display").TextContent.Trim());
    }

    // A schedule row answers "is there something to watch", so the useful fact is how far behind you
    // are - carried by the note - not a verdict on a show you are part-way through. Asserted rather
    // than just deleted, because a rating chip is the obvious thing to add back.
    [Fact]
    public void ACatalogedRowShowsNoRating()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.CatalogStatus, CatalogStatus.Watching)
            .Add(p => p.EpisodesWatched, 3)
            .Add(p => p.TotalEpisodes, 12)
            .Add(p => p.Note, "1 episode behind"));

        Assert.Empty(cut.FindAll(".rating-display"));

        // The facts that do belong there are still present.
        Assert.Single(cut.FindAll(".status-badge"));
        Assert.Equal("3 / 12", cut.Find(".progress-display").TextContent.Trim());
        Assert.Equal("1 episode behind", cut.Find(".schedule-episode__note").TextContent.Trim());
    }

    [Fact]
    public void WithoutACatalogStatus_ThereIsNoCatalogedTreatment()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren"));

        Assert.DoesNotContain("schedule-episode--cataloged", cut.Find("article").ClassList);
        Assert.Empty(cut.FindAll(".status-badge"));
    }

    // Dimming is only ever the filter the visitor chose. An episode having already aired used to fade
    // its row too, which meant that late in the week almost every column was faded - and it faded the
    // "behind" note with it. Asserted, because re-adding a time-based fade is an easy instinct.
    [Fact]
    public void OnlyTheChosenFilterDimsARow()
    {
        using var context = new BunitContext();

        // An episode well in the past is still rendered at full strength.
        var past = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830.AddYears(-1))
            .Add(p => p.Title, "A"));

        Assert.DoesNotContain("schedule-episode--dimmed", past.Find("article").ClassList);

        var dimmed = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "A")
            .Add(p => p.IsDimmed, true));

        Assert.Contains("schedule-episode--dimmed", dimmed.Find("article").ClassList);
    }

    [Fact]
    public void ABehindNoteIsColouredAsAWarning_AndCaughtUpAsASuccess()
    {
        using var context = new BunitContext();

        var behind = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "A")
            .Add(p => p.Note, "3 episodes behind")
            .Add(p => p.IsBehind, true));

        Assert.Contains("schedule-episode__note--behind", behind.Find(".schedule-episode__note").ClassList);

        var caughtUp = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "A")
            .Add(p => p.Note, "Caught up"));

        Assert.Contains("schedule-episode__note--caught-up", caughtUp.Find(".schedule-episode__note").ClassList);
    }

    // The column position is the only visual carrier of the day, so it has to be spelled out here.
    [Fact]
    public void TheSpokenLabelCarriesTheDayContextTheColumnConveysVisually()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren"));

        var spoken = cut.Find(".sr-only").TextContent;

        Assert.Contains("Monday", spoken);
        Assert.Contains("18:30", spoken);
    }

    [Fact]
    public void AnExternalHrefOpensInANewTabWithNoopener()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Href, "https://anilist.co/anime/1")
            .Add(p => p.IsExternal, true));

        var link = cut.Find("a.schedule-episode__link");

        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Contains("opens in a new tab", link.GetAttribute("aria-label"));
    }

    [Fact]
    public void AnInternalHrefRoutesWithoutOpeningATab()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Href, "anime/42"));

        var link = cut.Find("a.schedule-episode__link");

        Assert.Null(link.GetAttribute("target"));
        Assert.EndsWith("anime/42", link.GetAttribute("href"));
    }

    [Fact]
    public void WithNoHrefThereIsNoLinkAtAll()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(cut.FindAll(".schedule-episode__link"));
    }

    // The day name above is an h3, so episode titles have to be h4 or the heading order skips.
    [Fact]
    public void TheTitleIsAnH4_SoItNestsUnderTheDaysH3()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScheduleEpisodeRow>(parameters => parameters
            .Add(p => p.AirsAtLocal, Monday1830)
            .Add(p => p.Title, "Frieren"));

        Assert.Equal("H4", cut.Find(".schedule-episode__title").TagName);
    }
}
