using System.ComponentModel.DataAnnotations;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class AnimeEditorModelTests
{
    // The picker's cap keeps the UI from producing this, so the rule exists for rows that were
    // saved before the cap did. Opening one in the editor blocks the save until the status or the
    // count gives way -- which is the point.
    [Fact]
    public void WatchingEveryEpisodeWithoutCompleted_IsRejected()
    {
        var model = CreateModel();
        model.Status = CatalogStatus.Watching;
        model.EpisodesWatched = 25;

        var results = Validate(model);

        Assert.Contains(results, result =>
            result.ErrorMessage == "Every episode is watched, so the status should be Completed.");
    }

    [Theory]
    [InlineData(CatalogStatus.Planned)]
    [InlineData(CatalogStatus.OnHold)]
    [InlineData(CatalogStatus.Dropped)]
    public void WatchingEveryEpisodeUnderAnyOtherStatus_IsRejected(CatalogStatus status)
    {
        var model = CreateModel();
        model.Status = status;
        model.EpisodesWatched = 25;

        Assert.Contains(Validate(model), result =>
            result.ErrorMessage == "Every episode is watched, so the status should be Completed.");
    }

    [Fact]
    public void WatchingEveryEpisodeWhileCompleted_IsTheWholePoint()
    {
        var model = CreateModel();
        model.Status = CatalogStatus.Completed;
        model.EpisodesWatched = 25;

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void OneShortOfTheTotal_IsFine()
    {
        var model = CreateModel();
        model.Status = CatalogStatus.Watching;
        model.EpisodesWatched = 24;

        Assert.Empty(Validate(model));
    }

    // An airing show has no total to compare against, so the rule has nothing to say -- which is
    // what keeps AdminCatalogService's Completed-with-0-watched draft valid.
    [Fact]
    public void UnknownTotal_LeavesTheStatusAlone()
    {
        var model = CreateModel();
        model.Episodes = null;
        model.Status = CatalogStatus.Watching;
        model.EpisodesWatched = 999;

        Assert.Empty(Validate(model));
    }

    private static IReadOnlyList<ValidationResult> Validate(AnimeEditorModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static AnimeEditorModel CreateModel() => new()
    {
        TitleRomaji = "Code Geass",
        Episodes = 25,
        EpisodesWatched = 0,
        Status = CatalogStatus.Planned
    };
}
