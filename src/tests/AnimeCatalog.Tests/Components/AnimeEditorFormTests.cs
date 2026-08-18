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

    [Fact]
    public async Task SwitchingAwayFromCompleted_DoesNotEraseProgress()
    {
        await using var context = CreateContext();
        var model = CreateModel();

        var cut = context.Render<AnimeEditorForm>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Franchises, Array.Empty<Franchise>()));

        cut.FindAll(".status-picker__option")
            .Single(button => button.TextContent.Contains("Completed", StringComparison.Ordinal))
            .Click();
        cut.FindAll(".status-picker__option")
            .Single(button => button.TextContent.Contains("Watching", StringComparison.Ordinal))
            .Click();

        Assert.Equal(25, model.EpisodesWatched);
        Assert.Equal(CatalogStatus.Watching, model.Status);
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
