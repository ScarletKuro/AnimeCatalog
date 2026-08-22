using AnimeCatalog.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Components;

/// <summary>
/// Covers the two small navigation components, and specifically their NavLinkMatch choices - the
/// setting that is easy to get wrong in both directions and invisible until two things read as
/// active at once.
/// </summary>
public sealed class CalendarNavigationTests
{
    [Fact]
    public void TheTabsOfferBothViews()
    {
        using var context = new BunitContext();

        var cut = context.Render<CalendarViewTabs>();

        Assert.Equal(
            ["Airing week", "Archive"],
            cut.FindAll(".calendar-tabs__link").Select(link => link.TextContent.Trim()).ToArray());
    }

    // The airing tab needs Match=All: prefix matching would light it up on the archive route too, and
    // both tabs would claim to be active. Same trap as /admin versus /admin/add.
    [Fact]
    public void TheAiringTab_DoesNotClaimTheArchiveRoutesActiveState()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("calendar/archive/2011/spring");

        var cut = context.Render<CalendarViewTabs>();

        var active = Assert.Single(cut.FindAll("a.active"));
        Assert.Equal("Archive", active.TextContent.Trim());
    }

    [Fact]
    public void OnTheAiringRoute_OnlyTheAiringTabIsActive()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("calendar");

        var cut = context.Render<CalendarViewTabs>();

        var active = Assert.Single(cut.FindAll("a.active"));
        Assert.Equal("Airing week", active.TextContent.Trim());
    }

    [Fact]
    public void TheSeasonPickerOffersFourSeasonsPlusTheWholeYear()
    {
        using var context = new BunitContext();

        var cut = context.Render<SeasonPicker>(parameters => parameters.Add(p => p.Year, 2011));

        var links = cut.FindAll(".season-picker__option");

        Assert.Equal(
            ["Winter", "Spring", "Summer", "Fall", "Whole year"],
            links.Select(link => link.TextContent.Trim()).ToArray());
        Assert.All(links, link => Assert.Contains("calendar/archive/2011/", link.GetAttribute("href")));
    }

    [Fact]
    public void ExactlyOneSeasonIsMarkedActive()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("calendar/archive/2011/summer");

        var cut = context.Render<SeasonPicker>(parameters => parameters.Add(p => p.Year, 2011));

        var active = Assert.Single(cut.FindAll("a.active"));
        Assert.Equal("Summer", active.TextContent.Trim());
    }

    // The four hrefs are siblings at the same depth, so prefix matching is safe here - but only
    // because none of them is a prefix of another. Worth pinning.
    [Fact]
    public void NoSeasonClaimsAnothersActiveState()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("calendar/archive/2011/winter");

        var cut = context.Render<SeasonPicker>(parameters => parameters.Add(p => p.Year, 2011));

        Assert.Single(cut.FindAll("a.active"));
    }
}
