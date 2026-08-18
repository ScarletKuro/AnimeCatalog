using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class AnimeEditorFormTests
{
    [Fact]
    public void CompletedStatus_FillsWatchedEpisodesToTotalEpisodes()
    {
        using var context = new BunitContext();
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
    public void SwitchingAwayFromCompleted_DoesNotEraseProgress()
    {
        using var context = new BunitContext();
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
    public void ExistingFranchiseSuggestion_HidesWhenMissingAndShowsActualTitle()
    {
        using var context = new BunitContext();
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

    private static AnimeEditorModel CreateModel() => new()
    {
        TitleRomaji = "Code Geass",
        TitleEnglish = "Code Geass",
        Episodes = 25,
        EpisodesWatched = 0,
        Status = CatalogStatus.Planned
    };
}
