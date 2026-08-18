using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class StatMeterTests
{
    [Fact]
    public void ExposesProgressbarSemantics()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Episodes watched")
            .Add(p => p.Value, 12)
            .Add(p => p.Max, 24));

        var track = cut.Find(".stat-meter__track");
        Assert.Equal("progressbar", track.GetAttribute("role"));
        Assert.Equal("0", track.GetAttribute("aria-valuemin"));
        Assert.Equal("24", track.GetAttribute("aria-valuemax"));
        Assert.Equal("12", track.GetAttribute("aria-valuenow"));
        Assert.Equal("12 / 24", track.GetAttribute("aria-valuetext"));
    }

    [Theory]
    [InlineData(12, 24, "--meter-value:50%")]
    [InlineData(30, 24, "--meter-value:100%")]
    [InlineData(-5, 24, "--meter-value:0%")]
    public void FillWidth_IsClampedPercentage(double value, double max, string expected)
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Progress")
            .Add(p => p.Value, value)
            .Add(p => p.Max, max));

        Assert.Contains(expected, cut.Markup);
    }

    [Fact]
    public void ZeroMax_DoesNotDivideByZero()
    {
        using var context = new BunitContext();

        // A franchise with no entries, or an anime with no known episode count.
        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Entries completed")
            .Add(p => p.Value, 0)
            .Add(p => p.Max, 0));

        Assert.Contains("--meter-value:0%", cut.Markup);
        Assert.DoesNotContain("NaN", cut.Markup);
        Assert.DoesNotContain("Infinity", cut.Markup);
    }

    [Fact]
    public void ValueText_OverridesTheDefaultReadout()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Episodes watched")
            .Add(p => p.Value, 12)
            .Add(p => p.Max, 24)
            .Add(p => p.ValueText, "12 / ?"));

        Assert.Contains("12 / ?", cut.Markup);
        Assert.Equal("12 / ?", cut.Find(".stat-meter__track").GetAttribute("aria-valuetext"));
    }

    [Fact]
    public void Variant_AppliesFillModifier()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Completed")
            .Add(p => p.Variant, "success"));

        Assert.Contains("stat-meter__fill--success", cut.Markup);
    }

    [Fact]
    public void HeadIsLinkedToTheTrackByLabelId()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Episodes watched"));

        var labelId = cut.Find(".stat-meter__label").GetAttribute("id");
        Assert.False(string.IsNullOrWhiteSpace(labelId));
        Assert.Equal(labelId, cut.Find(".stat-meter__track").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void WithoutHead_LabelMovesToAriaLabel()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Episodes watched")
            .Add(p => p.ShowHead, false));

        Assert.Empty(cut.FindAll(".stat-meter__head"));

        var track = cut.Find(".stat-meter__track");
        Assert.Equal("Episodes watched", track.GetAttribute("aria-label"));
        Assert.Null(track.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Caption_RendersOnlyWhenSupplied()
    {
        using var context = new BunitContext();

        var without = context.Render<StatMeter>(parameters => parameters.Add(p => p.Label, "Progress"));
        Assert.Empty(without.FindAll(".stat-meter__caption"));

        var with = context.Render<StatMeter>(parameters => parameters
            .Add(p => p.Label, "Progress")
            .Add(p => p.Caption, "60% finished."));
        Assert.Contains("60% finished.", with.Markup);
    }
}
