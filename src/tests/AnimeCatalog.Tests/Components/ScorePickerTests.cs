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
    public void ClearButton_EmitsNull()
    {
        using var context = new BunitContext();

        decimal? selectedScore = 5;
        var cut = context.Render<ScorePicker>(parameters => parameters
            .Add(p => p.Value, 5m)
            .Add(p => p.ValueChanged, value => selectedScore = value));

        cut.Find(".score-picker__clear").Click();

        Assert.Null(selectedScore);
    }

    [Fact]
    public void ExistingScore_RendersFilledStars()
    {
        using var context = new BunitContext();
        var cut = context.Render<ScorePicker>(parameters => parameters.Add(p => p.Value, 7m));

        Assert.Equal(7, cut.FindAll(".score-picker__option--filled").Count);
        Assert.Contains("7 / 10", cut.Markup);
    }
}
