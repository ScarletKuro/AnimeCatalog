using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class StatusBarTests
{
    [Fact]
    public void EmptyStatuses_AreLeftOutOfBothBarAndLegend()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatusBar>(parameters => parameters
            .Add(p => p.Breakdown, FullBreakdown(planned: 0, watching: 2, completed: 6, onHold: 0, dropped: 0)));

        // A zero-width segment is invisible but would still be announced through the legend.
        Assert.Equal(2, cut.FindAll(".status-bar__segment").Count);
        Assert.Equal(2, cut.FindAll(".status-bar__key").Count);
        Assert.DoesNotContain("status-bar__segment--planned", cut.Markup);
        Assert.DoesNotContain("status-bar__dot--dropped", cut.Markup);
    }

    [Fact]
    public void SegmentWidths_AreSharesOfTheTotal()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatusBar>(parameters => parameters
            .Add(p => p.Breakdown, FullBreakdown(planned: 0, watching: 2, completed: 6, onHold: 0, dropped: 0)));

        Assert.Contains("--seg:25%", cut.Markup);
        Assert.Contains("--seg:75%", cut.Markup);
    }

    [Fact]
    public void ExplicitTotal_OverridesTheSumOfTheBreakdown()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatusBar>(parameters => parameters
            .Add(p => p.Breakdown, FullBreakdown(planned: 0, watching: 0, completed: 5, onHold: 0, dropped: 0))
            .Add(p => p.Total, 10));

        Assert.Contains("--seg:50%", cut.Markup);
    }

    [Fact]
    public void AccessibleName_ListsTheCounts()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatusBar>(parameters => parameters
            .Add(p => p.Breakdown, FullBreakdown(planned: 1, watching: 2, completed: 0, onHold: 0, dropped: 0)));

        var bar = cut.Find(".status-bar");
        Assert.Equal("img", bar.GetAttribute("role"));
        Assert.Equal("1 planned, 2 watching", bar.GetAttribute("aria-label"));
    }

    [Fact]
    public void EmptyBreakdown_RendersNoSegmentsAndDoesNotDivideByZero()
    {
        using var context = new BunitContext();

        var cut = context.Render<StatusBar>(parameters => parameters
            .Add(p => p.Breakdown, FullBreakdown(0, 0, 0, 0, 0)));

        Assert.Empty(cut.FindAll(".status-bar__segment"));
        Assert.Empty(cut.FindAll(".status-bar__key"));
        Assert.DoesNotContain("NaN", cut.Markup);
        Assert.Equal(string.Empty, cut.Find(".status-bar").GetAttribute("aria-label"));
    }

    /// <summary>All five statuses in enum order, the shape the services always produce.</summary>
    private static IReadOnlyList<StatusCount> FullBreakdown(int planned, int watching, int completed, int onHold, int dropped) =>
    [
        new(CatalogStatus.Planned, planned),
        new(CatalogStatus.Watching, watching),
        new(CatalogStatus.Completed, completed),
        new(CatalogStatus.OnHold, onHold),
        new(CatalogStatus.Dropped, dropped)
    ];
}
