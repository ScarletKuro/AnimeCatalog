using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ScorePickerTests
{
    [Fact]
    public void ClickingStars_EmitsMatchingScore()
    {
        using var context = new BunitContext();

        decimal? selectedScore = null;
        var cut = context.Render<ScorePicker>(parameters => parameters
            .Add(p => p.Value, null)
            .Add(p => p.ValueChanged, value => selectedScore = value));

        for (var index = 0; index < 10; index++)
        {
            cut.FindAll(".score-picker__option")[index].Click();
            Assert.Equal(index + 1, selectedScore);
        }
    }


    [Fact]
    public void ExistingScore_RendersFilledStars()
    {
        using var context = new BunitContext();
        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, 7m));

        Assert.Equal(7, cut.FindAll(".score-picker__option--filled").Count);
        Assert.Contains("7 / 10", cut.Markup);
    }

    [Fact]
    public void ClickingTheSelectedStar_ClearsTheScore()
    {
        using var context = new BunitContext();

        decimal? selectedScore = 7;
        var cut = context.Render<ScorePicker>(parameters => parameters
            .Add(p => p.Value, 7m)
            .Add(p => p.ValueChanged, value => selectedScore = value));

        cut.FindAll(".score-picker__option")[6].Click();

        Assert.Null(selectedScore);
    }

    // The control is now ten stars and nothing else, in either state.
    [Fact]
    public void Unrated_RendersStarsOnly()
    {
        using var context = new BunitContext();
        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, null));

        Assert.Equal(10, cut.FindAll(".score-picker__option").Count);
        Assert.Empty(cut.FindAll(".score-picker__option--filled"));
        Assert.Contains("Unrated", cut.Markup);
    }

    // The stars render from a private field, not from the parameter, so this pins the other half
    // of that contract: whatever the parent pushes still wins over a local selection.
    [Fact]
    public void ParentValue_WinsOverALocalSelection()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, 3m));

        cut.FindAll(".score-picker__option")[7].Click();
        Assert.Equal(8, cut.FindAll(".score-picker__option--filled").Count);

        cut.Render(parameters => parameters.Add(p => p.Value, 3m));

        Assert.Equal(3, cut.FindAll(".score-picker__option--filled").Count);
    }

    // Clicking again is the only way back to unrated, so it has to be said somewhere.
    [Fact]
    public void ExistingScore_AdvertisesTheToggle()
    {
        using var context = new BunitContext();
        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, 7m));

        var selected = cut.FindAll(".score-picker__option")[6];

        Assert.Contains("click 7 again to clear", cut.Markup);
        Assert.Equal("Click to clear", selected.GetAttribute("title"));
        Assert.Equal("Clear score", selected.GetAttribute("aria-label"));
    }
}
