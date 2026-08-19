using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Components;

public sealed class AnimeEditorFormTests
{
    [Fact]
    public async Task CompletedStatus_FillsWatchedEpisodesToTotalEpisodes()
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll(".status-picker__option")
            .Single(button => button.TextContent.Contains("Completed", StringComparison.Ordinal))
            .Click();

        Assert.Equal(25, model.EpisodesWatched);
        Assert.Equal(CatalogStatus.Completed, model.Status);
    }

    // Every episode watched is what Completed means, so leaving it cannot leave the count on the
    // total -- the entry would claim you are still working through a show you have finished.
    [Fact]
    public async Task SwitchingAwayFromCompleted_StepsTheCountOffTheTotal()
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Status(cut, "Completed").Click();
        Assert.Equal(25, model.EpisodesWatched);

        Status(cut, "Watching").Click();

        Assert.Equal(24, model.EpisodesWatched);
        Assert.Equal(CatalogStatus.Watching, model.Status);
    }

    [Theory]
    [InlineData("Planned")]
    [InlineData("On Hold")]
    [InlineData("Dropped")]
    public async Task SwitchingFromCompletedToAnyOtherStatus_StepsTheCountOffTheTotal(string label)
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Status(cut, "Completed").Click();
        Status(cut, label).Click();

        Assert.Equal(24, model.EpisodesWatched);
    }

    // Clamp, never assign: the step-off must not invent progress for an entry that had none.
    [Fact]
    public async Task SwitchingAwayFromCompleted_LeavesACountBelowTheTotalAlone()
    {
        await using var context = CreateContext();
        var model = CreateModel();
        model.Status = CatalogStatus.Completed;
        model.EpisodesWatched = 0;

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Status(cut, "Watching").Click();

        Assert.Equal(0, model.EpisodesWatched);
    }

    [Fact]
    public async Task ExistingFranchiseSuggestion_HidesWhenMissingAndShowsActualTitle()
    {
        await using var context = CreateContext();
        var hiddenSuggestionModel = CreateModel();
        hiddenSuggestionModel.FranchiseAssignmentMode = FranchiseAssignmentMode.Existing;
        hiddenSuggestionModel.SuggestedFranchiseTitle = null;

        var hiddenCut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, hiddenSuggestionModel)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Assert.DoesNotContain("Suggested:", hiddenCut.Markup);
        Assert.DoesNotContain("_model.SuggestedFranchiseTitle", hiddenCut.Markup);

        var shownSuggestionModel = CreateModel();
        shownSuggestionModel.FranchiseAssignmentMode = FranchiseAssignmentMode.Existing;
        shownSuggestionModel.SuggestedFranchiseTitle = "Code Geass";

        var shownCut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, shownSuggestionModel)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Assert.Contains("Suggested: Code Geass", shownCut.Markup);
    }

    [Fact]
    public async Task CorrectingTotalEpisodesWhileCompleted_KeepsWatchedCountAtTheNewTotal()
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll(".status-picker__option")
            .Single(button => button.TextContent.Contains("Completed", StringComparison.Ordinal))
            .Click();

        // The picker offers no range while Completed, so nothing else would catch the drift.
        cut.FindAll("input[type=number]")
            .First(input => input.GetAttribute("value") == "25")
            .Change("26");

        Assert.Equal(26, model.Episodes);
        Assert.Equal(26, model.EpisodesWatched);
    }

    // The same handler runs while not Completed, where it has to clamp instead of follow: the
    // picker stops one short of the total, so a corrected-down total drags the count with it.
    [Fact]
    public async Task CorrectingTotalEpisodesWhileWatching_ClampsWatchedBelowTheNewTotal()
    {
        await using var context = CreateContext();
        var model = CreateModel();
        model.Status = CatalogStatus.Watching;
        model.EpisodesWatched = 20;

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll("input[type=number]")
            .First(input => input.GetAttribute("value") == "25")
            .Change("12");

        Assert.Equal(12, model.Episodes);
        Assert.Equal(11, model.EpisodesWatched);
    }

    // The loop the coupling has to close: finishing the last episode marks the entry Completed and
    // puts the field away, and coming back out steps the count off the total so the two never
    // disagree in either direction.
    [Fact]
    public async Task PickingTheLastEpisode_CompletesTheEntryAndHidesTheField()
    {
        await using var context = CreateContext();
        var model = CreateModel();
        model.Status = CatalogStatus.Watching;

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll(".episode-picker__option")[^1].Click();

        Assert.Equal(25, model.EpisodesWatched);
        Assert.Equal(CatalogStatus.Completed, model.Status);
        Assert.Empty(cut.FindAll(".episode-picker__option"));

        Status(cut, "Watching").Click();

        Assert.Equal(24, model.EpisodesWatched);
        Assert.NotEmpty(cut.FindAll(".episode-picker__option"));
    }

    [Fact]
    public async Task PickingAnyOtherEpisode_LeavesTheStatusAlone()
    {
        await using var context = CreateContext();
        var model = CreateModel();
        model.Status = CatalogStatus.Watching;

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll(".episode-picker__option")[24].Click();

        Assert.Equal(24, model.EpisodesWatched);
        Assert.Equal(CatalogStatus.Watching, model.Status);
    }

    // Status sits directly above the count it is coupled to, and the score follows both.
    [Fact]
    public async Task EntryDetails_OrdersStatusThenEpisodesWatchedThenScore()
    {
        await using var context = CreateContext();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, CreateModel())
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        var markup = cut.Markup;

        Assert.True(markup.IndexOf("status-picker", StringComparison.Ordinal)
            < markup.IndexOf("episode-picker", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("episode-picker", StringComparison.Ordinal)
            < markup.IndexOf("score-picker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletedStatus_DropsTheEpisodesWatchedFieldAndRestoresItOnLeaving()
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        Assert.NotEmpty(cut.FindAll(".episode-picker__option"));

        Status(cut, "Completed").Click();

        // Label and all: Completed plus the Episodes field already state the count.
        Assert.Empty(cut.FindAll(".episode-picker__option"));
        Assert.DoesNotContain("Episodes watched", cut.Markup);

        Status(cut, "Watching").Click();

        Assert.NotEmpty(cut.FindAll(".episode-picker__option"));
        Assert.Contains("Episodes watched", cut.Markup);
    }

    private static IElement Status(IRenderedComponent<AnimeEditorForm> cut, string label) =>
        cut.FindAll(".status-picker__option")
            .Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    // EpisodePicker reaches for BrowserStorageService to keep the selected option inside its
    // scrolled track, so the form now needs the same JS plumbing the page tests use.
    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        return context;
    }

    private static AnimeEditorModel CreateModel() => new()
    {
        TitleRomaji = "Code Geass",
        TitleEnglish = "Code Geass",
        Episodes = 25,
        EpisodesWatched = 0,
        Status = CatalogStatus.Planned
    };
}
