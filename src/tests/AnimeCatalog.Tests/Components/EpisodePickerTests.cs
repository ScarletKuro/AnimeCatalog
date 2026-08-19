using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Components;

public sealed class EpisodePickerTests
{
    [Fact]
    public async Task ClickingOption_EmitsMatchingEpisodeCount()
    {
        await using var context = CreateContext();

        var selected = -1;
        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 5)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        var options = cut.FindAll(".episode-picker__option");
        Assert.Equal(13, options.Count);

        options[9].Click();
        Assert.Equal(9, selected);
    }

    [Fact]
    public async Task ZeroOption_EmitsZero()
    {
        await using var context = CreateContext();

        var selected = -1;
        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 4)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        cut.FindAll(".episode-picker__option")[0].Click();

        Assert.Equal(0, selected);
    }

    [Fact]
    public async Task ExistingValue_FillsEveryWatchedEpisodeAndShowsProgress()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 7)
            .Add(p => p.Max, 25));

        Assert.Equal(7, cut.FindAll(".episode-picker__option--watched").Count);
        Assert.Single(cut.FindAll(".episode-picker__option--selected"));
        Assert.Contains("Watched through episode 7 · next up 8 · 18 left", cut.Markup);
    }

    // The whole point of the field: "7" has to be unambiguous about whether episode 7 is done or
    // is the one coming up. The summary says it, and the dashed cell shows it.
    [Fact]
    public async Task NextEpisode_IsMarkedRightAfterTheSelection()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 4)
            .Add(p => p.Max, 12));

        var next = Assert.Single(cut.FindAll(".episode-picker__option--next"));
        Assert.Equal("5", next.TextContent.Trim());
        Assert.DoesNotContain("episode-picker__option--watched", next.ClassName);
    }

    // The last episode is on offer -- selecting it is how an entry becomes Completed. Hosts react
    // to the emitted count; the picker itself just reports it.
    [Fact]
    public async Task LastEpisode_IsOfferedAndEmitsTheTotal()
    {
        await using var context = CreateContext();

        var selected = -1;
        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 4)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        var options = cut.FindAll(".episode-picker__option");
        Assert.Equal("12", options[^1].TextContent.Trim());

        options[^1].Click();

        Assert.Equal(12, selected);
    }

    // Selecting the last episode marks the entry Completed and puts the field away, so a visible
    // track sitting on the total is a row that predates that rule. The readout names the fix.
    [Fact]
    public async Task WatchedTheTotal_AsksForCompleted()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 12)
            .Add(p => p.Max, 12));

        Assert.Empty(cut.FindAll(".episode-picker__option--next"));
        Assert.Equal("12", cut.Find(".episode-picker__option--selected").TextContent.Trim());
        Assert.Contains("All 12 watched · set the status to Completed", cut.Markup);
    }

    // The rule both editors share for the count -> status direction.
    [Theory]
    // The last episode finishes the show, whatever the status said before.
    [InlineData(12, CatalogStatus.Watching, 12, CatalogStatus.Completed)]
    [InlineData(12, CatalogStatus.Planned, 12, CatalogStatus.Completed)]
    [InlineData(12, CatalogStatus.OnHold, 12, CatalogStatus.Completed)]
    [InlineData(12, CatalogStatus.Dropped, 12, CatalogStatus.Completed)]
    // A stale row already past the total is finished too.
    [InlineData(30, CatalogStatus.Watching, 25, CatalogStatus.Completed)]
    // Anything short of the total leaves the status alone.
    [InlineData(11, CatalogStatus.Watching, 12, CatalogStatus.Watching)]
    [InlineData(0, CatalogStatus.Planned, 12, CatalogStatus.Planned)]
    // No total to reach, so nothing to promote -- an airing show never finishes itself.
    [InlineData(999, CatalogStatus.Watching, null, CatalogStatus.Watching)]
    [InlineData(0, CatalogStatus.Watching, 0, CatalogStatus.Watching)]
    public void ReconcileStatus_PromotesOnlyOnTheLastEpisode(
        int watched, CatalogStatus status, int? max, CatalogStatus expected)
    {
        Assert.Equal(expected, EpisodePicker.ReconcileStatus(watched, status, max));
    }

    [Fact]
    public async Task ZeroOption_ReadsAsNoneAndPointsAtTheFirstEpisode()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 0)
            .Add(p => p.Max, 12));

        Assert.Equal("None", cut.FindAll(".episode-picker__option")[0].TextContent.Trim());
        Assert.Contains("Nothing watched yet · start with episode 1", cut.Markup);
        Assert.Equal("1", cut.Find(".episode-picker__option--next").TextContent.Trim());
    }

    // Ceiling stretches for these rows, so a Ceiling-based summary would claim the entry is
    // finished when the recorded total says otherwise.
    [Fact]
    public async Task WatchedBeyondTotal_SaysTheCountOutrunsTheRecordedTotal()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 30)
            .Add(p => p.Max, 25));

        Assert.Contains("Watched through episode 30 · more than the recorded total of 25", cut.Markup);
        Assert.DoesNotContain("All 25 episodes watched", cut.Markup);
    }

    [Fact]
    public async Task UnknownTotal_StillStatesWhatTheNumberMeans()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 3)
            .Add(p => p.Max, null));

        var hint = Assert.Single(cut.FindAll(".episode-picker__hint"));
        Assert.Equal("Pick the last episode you finished.", hint.TextContent.Trim());
        Assert.Equal(hint.Id, cut.Find("input[type=number]").GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task UnknownTotal_FallsBackToNumberField()
    {
        await using var context = CreateContext();

        var selected = -1;
        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 3)
            .Add(p => p.Max, null)
            .Add(p => p.ValueChanged, value => selected = value));

        Assert.Empty(cut.FindAll(".episode-picker__options"));

        var input = cut.Find("input[type=number]");
        input.Change("9");

        Assert.Equal(9, selected);
    }

    [Fact]
    public async Task WatchedBeyondTotal_StretchesRangeSoCurrentValueStaysSelectable()
    {
        await using var context = CreateContext();

        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 30)
            .Add(p => p.Max, 25));

        Assert.Equal(31, cut.FindAll(".episode-picker__option").Count);
        Assert.Single(cut.FindAll(".episode-picker__option--selected"));
    }

    [Theory]
    [InlineData("ArrowRight", 6)]
    [InlineData("ArrowLeft", 4)]
    [InlineData("Home", 0)]
    [InlineData("End", 12)]
    public async Task KeyboardNavigation_MovesSelection(string key, int expected)
    {
        await using var context = CreateContext();

        var selected = -1;
        var cut = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 5)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        cut.Find(".episode-picker__options").KeyDown(key);

        Assert.Equal(expected, selected);
    }

    [Fact]
    public async Task KeyboardNavigation_ClampsAtBothEnds()
    {
        await using var context = CreateContext();

        var selected = -1;
        var atStart = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 0)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        atStart.Find(".episode-picker__options").KeyDown("ArrowLeft");
        Assert.Equal(-1, selected);

        var atEnd = context.Render<EpisodePicker>(parameters => parameters
            .Add(p => p.Value, 12)
            .Add(p => p.Max, 12)
            .Add(p => p.ValueChanged, value => selected = value));

        atEnd.Find(".episode-picker__options").KeyDown("ArrowRight");
        Assert.Equal(-1, selected);
    }

    // The rule both editors share. AnimeDetails calls this and nothing else for the coupling, so
    // covering it here covers the page handler that has no harness of its own.
    [Theory]
    // Completed means every episode, whatever the count was.
    [InlineData(0, CatalogStatus.Completed, 25, 25)]
    [InlineData(7, CatalogStatus.Completed, 25, 25)]
    // Anything else means not every episode, so the count steps off the total.
    [InlineData(25, CatalogStatus.Watching, 25, 24)]
    [InlineData(25, CatalogStatus.Planned, 25, 24)]
    [InlineData(25, CatalogStatus.OnHold, 25, 24)]
    [InlineData(25, CatalogStatus.Dropped, 25, 24)]
    // Clamp, never raise: a count already below the total is left where it is.
    [InlineData(0, CatalogStatus.Watching, 25, 0)]
    [InlineData(7, CatalogStatus.Watching, 25, 7)]
    // A stale row whose total was corrected downward comes back inside the range.
    [InlineData(30, CatalogStatus.Watching, 25, 24)]
    // No total to measure against, so nothing to reconcile.
    [InlineData(999, CatalogStatus.Watching, null, 999)]
    [InlineData(0, CatalogStatus.Completed, null, 0)]
    // A single-episode entry has no room below the total.
    [InlineData(1, CatalogStatus.Watching, 1, 0)]
    [InlineData(3, CatalogStatus.Watching, 0, 0)]
    public void ReconcileWatchedCount_KeepsTheStatusAndTheCountAgreeing(
        int watched, CatalogStatus status, int? max, int expected)
    {
        Assert.Equal(expected, EpisodePicker.ReconcileWatchedCount(watched, status, max));
    }

    [Theory]
    [InlineData(CatalogStatus.Watching, 25, true)]
    [InlineData(CatalogStatus.Dropped, 25, true)]
    [InlineData(CatalogStatus.Completed, 25, false)]
    // A Completed entry with no known total has nothing for the auto-fill to copy, so the count
    // has to stay editable or it can never be recorded.
    [InlineData(CatalogStatus.Completed, null, true)]
    public void IsEditable_HidesTheCountOnlyWhenCompletedImpliesIt(CatalogStatus status, int? max, bool expected)
    {
        Assert.Equal(expected, EpisodePicker.IsEditable(status, max));
    }

    // The picker reaches for BrowserStorageService to keep the selected option inside the
    // scrolled track, so component tests need the same JS plumbing the page tests use.
    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        return context;
    }
}
