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
        Assert.Contains("7 / 25", cut.Markup);
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
