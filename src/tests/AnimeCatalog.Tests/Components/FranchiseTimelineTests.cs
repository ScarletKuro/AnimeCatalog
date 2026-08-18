using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class FranchiseTimelineTests
{
    [Fact]
    public void EmptyGroups_RenderAnEmptyNote()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, Array.Empty<FranchiseTimelineGroup>()));

        Assert.Contains("empty-note", cut.Markup);
        Assert.Empty(cut.FindAll(".timeline"));
    }

    [Fact]
    public void RendersOneGroupPerYearInSuppliedOrder()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups,
            [
                Group(2011, Entry(1, "Fate/Zero")),
                Group(2014, Entry(2, "Unlimited Blade Works")),
                Group(null, Entry(3, "Unknown special"))
            ]));

        var years = cut.FindAll(".timeline__year").Select(node => node.TextContent).ToList();
        Assert.Equal(["2011", "2014", "Unknown"], years);
    }

    [Fact]
    public void YearHeadingIsLevelThree()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(1, "Fate/Zero"))]));

        Assert.Equal("H3", cut.Find(".timeline__year").TagName);
    }

    [Fact]
    public void EntryLinksToItsLocalAnimePage()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(7, "Fate/Zero"))]));

        Assert.Equal("anime/7", cut.Find(".timeline__title").GetAttribute("href"));
    }

    [Fact]
    public void RendersSeasonAndFormatChipsWhenKnown()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(1, "Fate/Zero", season: "FALL", format: "TV"))]));

        Assert.Contains("Fall", cut.Markup);
        Assert.Contains("TV", cut.Markup);
    }

    [Fact]
    public void OmitsChipsWhenSeasonAndFormatAreUnknown()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(1, "Fate/Zero"))]));

        // Only the status badge should be present, not season or format chips.
        Assert.Empty(cut.FindAll(".chip"));
        Assert.Contains("status-badge", cut.Markup);
    }

    [Fact]
    public void RendersEveryEntryInAGroup()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(1, "First"), Entry(2, "Second"))]));

        Assert.Equal(2, cut.FindAll(".timeline__item").Count);
    }

    [Fact]
    public void MarkerIsDecorative()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseTimeline>(parameters => parameters
            .Add(p => p.Groups, [Group(2011, Entry(1, "Fate/Zero"))]));

        Assert.Equal("true", cut.Find(".timeline__marker").GetAttribute("aria-hidden"));
    }

    private static FranchiseTimelineGroup Group(int? year, params AnimeListItemViewModel[] entries) =>
        new() { Year = year, Entries = entries };

    private static AnimeListItemViewModel Entry(
        long id,
        string title,
        string? season = null,
        string? format = null) => new()
        {
            AnimeEntry = new AnimeEntry
            {
                Id = id,
                TitleRomaji = title,
                Season = season,
                Format = format
            },
            CatalogEntry = new CatalogEntry
            {
                AnimeEntryId = id,
                Status = CatalogStatus.Completed
            }
        };
}
