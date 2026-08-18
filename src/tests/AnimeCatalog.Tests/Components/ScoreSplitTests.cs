using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ScoreSplitTests
{
    // The whole point of this component is that a personal /10 score can never be mistaken for
    // AniList's /100 one, so the disambiguators are what the tests guard.
    [Fact]
    public void ShowsBothUnitsSoTheScalesCannotBeConfused()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>(parameters => parameters
            .Add(p => p.PersonalScore, 9.5m)
            .Add(p => p.CommunityScore, 88));

        Assert.Contains("/ 10", cut.Markup);
        Assert.Contains("/ 100", cut.Markup);
        Assert.Contains("9.5", cut.Markup);
        Assert.Contains("88", cut.Markup);
    }

    [Fact]
    public void OnlyThePersonalCellCarriesTheStar()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>(parameters => parameters
            .Add(p => p.PersonalScore, 9.5m)
            .Add(p => p.CommunityScore, 88));

        var personal = cut.Find(".score-split__cell--personal");
        var community = cut.Find(".score-split__cell--community");

        Assert.Contains("<svg", personal.InnerHtml);
        Assert.DoesNotContain("<svg", community.InnerHtml);
    }

    [Fact]
    public void LabelsNameTheirOwner()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>(parameters => parameters
            .Add(p => p.PersonalScore, 8m)
            .Add(p => p.CommunityScore, 80));

        Assert.Contains("Your score", cut.Markup);
        Assert.Contains("AniList average", cut.Markup);
    }

    [Fact]
    public void LabelsAreOverridable()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>(parameters => parameters
            .Add(p => p.PersonalLabel, "Your average")
            .Add(p => p.CommunityLabel, "AniList average"));

        Assert.Contains("Your average", cut.Markup);
    }

    [Fact]
    public void MissingScores_RenderAnEmDashNotZero()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>();

        Assert.Contains("—", cut.Markup);
        Assert.DoesNotContain(">0<", cut.Markup);
        Assert.DoesNotContain("0.0", cut.Markup);
    }

    [Fact]
    public void CellsCarryDistinctModifiers()
    {
        using var context = new BunitContext();

        var cut = context.Render<ScoreSplit>();

        Assert.Contains("score-split__cell--personal", cut.Markup);
        Assert.Contains("score-split__cell--community", cut.Markup);
    }
}
