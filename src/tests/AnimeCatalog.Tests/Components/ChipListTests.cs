using AnimeCatalog.Components;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ChipListTests
{
    [Fact]
    public void EmptyItems_RenderNothing()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, Array.Empty<ChipItem>()));

        // No stray empty <ul>: callers own their own empty state.
        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void RendersOneListItemPerChip()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action"), new("Drama"), new("Fantasy") }));

        Assert.Equal(3, cut.FindAll("li").Count);
        Assert.Contains("Fantasy", cut.Markup);
    }

    [Fact]
    public void Label_BecomesTheListAccessibleName()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action") })
            .Add(p => p.Label, "Genres"));

        Assert.Equal("Genres", cut.Find("ul").GetAttribute("aria-label"));
    }

    [Fact]
    public void Value_RendersAsASecondarySpan()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action", "×4") }));

        Assert.Equal("×4", cut.Find(".chip__value").TextContent);
    }

    [Fact]
    public void Rank_RendersDecorativeBarPlusScreenReaderText()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Time Skip", Rank: 87) }));

        var bar = cut.Find(".chip__rank");
        Assert.Contains("--rank:87%", bar.GetAttribute("style"));
        Assert.Equal("true", bar.GetAttribute("aria-hidden"));
        Assert.Contains("87% relevance", cut.Find(".sr-only").TextContent);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-10)]
    public void Rank_IsClamped(int rank)
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Tag", Rank: rank) }));

        var style = cut.Find(".chip__rank").GetAttribute("style");
        Assert.True(style!.Contains("--rank:100%") || style.Contains("--rank:0%"));
    }

    [Fact]
    public void Href_RendersAnAnchor()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Bones", Href: "https://anilist.co/studio/4") }));

        var anchor = cut.Find("a.chip");
        Assert.Equal("https://anilist.co/studio/4", anchor.GetAttribute("href"));
        Assert.Contains("chip--link", anchor.GetAttribute("class"));
    }

    [Fact]
    public void NoHref_RendersASpan()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action") }));

        Assert.Empty(cut.FindAll("a"));
        Assert.NotNull(cut.Find("span.chip"));
    }

    [Fact]
    public void ExternalLink_OpensNewTabSafely()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Bones", Href: "https://anilist.co/studio/4", IsExternal: true) }));

        var anchor = cut.Find("a.chip");
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Contains("noopener", anchor.GetAttribute("rel"));
        Assert.Contains("noreferrer", anchor.GetAttribute("rel"));
        Assert.Contains("opens in a new tab", cut.Markup);
    }

    [Fact]
    public void Max_TruncatesAndAppendsOverflowChip()
    {
        using var context = new BunitContext();

        var items = Enumerable.Range(1, 10).Select(index => new ChipItem($"Tag {index}")).ToArray();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.Max, 4));

        // Four visible chips plus the overflow chip.
        Assert.Equal(5, cut.FindAll("li").Count);
        Assert.Contains("+6", cut.Markup);
        Assert.DoesNotContain("Tag 9", cut.Markup);
    }

    [Fact]
    public void Max_LargerThanTheListAddsNoOverflowChip()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action"), new("Drama") })
            .Add(p => p.Max, 5));

        Assert.Equal(2, cut.FindAll("li").Count);
        Assert.DoesNotContain("chip--muted", cut.Markup);
    }

    [Fact]
    public void Variant_AppliesModifierClass()
    {
        using var context = new BunitContext();

        var cut = context.Render<ChipList>(parameters => parameters
            .Add(p => p.Items, new ChipItem[] { new("Action", Variant: "accent") }));

        Assert.Contains("chip--accent", cut.Markup);
    }
}
