using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class BannerHeroTests
{
    [Fact]
    public void MissingBanner_FallsBackToGradient()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Contains("banner-hero--no-banner", cut.Markup);
        Assert.Empty(cut.FindAll(".banner-hero__banner"));
    }

    [Fact]
    public void Banner_IsDecorativeBecauseTheTitleIsAdjacentText()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.BannerUrl, "https://example.test/banner.jpg"));

        Assert.Equal(string.Empty, cut.Find(".banner-hero__banner").GetAttribute("alt"));
        Assert.DoesNotContain("banner-hero--no-banner", cut.Markup);
    }

    [Fact]
    public void MissingPoster_RendersLetterFallback()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "astarotte's toy"));

        Assert.Contains("poster-fallback", cut.Markup);
        Assert.Contains(">A<", cut.Markup);
    }

    [Fact]
    public void Poster_GetsDescriptiveAltDerivedFromTheTitle()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.PosterUrl, "https://example.test/cover.jpg"));

        Assert.Equal("Frieren cover", cut.Find(".banner-hero__poster img").GetAttribute("alt"));
    }

    [Fact]
    public void PosterAlt_IsOverridable()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.PosterUrl, "https://example.test/cover.jpg")
            .Add(p => p.PosterAlt, "Franchise artwork"));

        Assert.Equal("Franchise artwork", cut.Find(".banner-hero__poster img").GetAttribute("alt"));
    }

    [Fact]
    public void RendersExactlyOneLevelOneHeading()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        var headings = cut.FindAll("h1");
        Assert.Single(headings);
        Assert.Equal("Frieren", headings[0].TextContent);
    }

    [Fact]
    public void OptionalSlotsRenderOnlyWhenSupplied()
    {
        using var context = new BunitContext();

        var bare = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(bare.FindAll(".banner-hero__badges"));
        Assert.Empty(bare.FindAll(".banner-hero__meta"));
        Assert.Empty(bare.FindAll(".banner-hero__aside"));
        Assert.Empty(bare.FindAll(".banner-hero__actions"));
        Assert.Empty(bare.FindAll(".eyebrow"));
        Assert.Empty(bare.FindAll(".banner-hero__subtitle"));

        var full = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Eyebrow, "TV")
            .Add(p => p.Subtitle, "Sousou no Frieren")
            .Add(p => p.Badges, builder => builder.AddMarkupContent(0, "<span>badge</span>"))
            .Add(p => p.MetaContent, builder => builder.AddMarkupContent(0, "<span>meta</span>"))
            .Add(p => p.SideContent, builder => builder.AddMarkupContent(0, "<span>aside</span>"))
            .Add(p => p.Actions, builder => builder.AddMarkupContent(0, "<span>action</span>")));

        Assert.Contains("badge", full.Find(".banner-hero__badges").InnerHtml);
        Assert.Contains("meta", full.Find(".banner-hero__meta").InnerHtml);
        Assert.Contains("aside", full.Find(".banner-hero__aside").InnerHtml);
        Assert.Contains("action", full.Find(".banner-hero__actions").InnerHtml);
        Assert.Contains("Sousou no Frieren", full.Markup);
        Assert.Contains("TV", full.Markup);
    }

    [Fact]
    public void Toolbar_RendersOnlyWhenSuppliedAndIsALabelledGroup()
    {
        using var context = new BunitContext();

        var without = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren"));
        Assert.Empty(without.FindAll(".banner-hero__toolbar"));

        var with = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.ToolbarLabel, "Admin actions")
            .Add(p => p.Toolbar, builder => builder.AddMarkupContent(0, "<button>Delete</button>")));

        var toolbar = with.Find(".banner-hero__toolbar");
        Assert.Equal("group", toolbar.GetAttribute("role"));
        Assert.Equal("Admin actions", toolbar.GetAttribute("aria-label"));
        Assert.Contains("Delete", toolbar.InnerHtml);
    }

    [Fact]
    public void ScrimIsHiddenFromAssistiveTechnology()
    {
        using var context = new BunitContext();

        var cut = context.Render<BannerHero>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Equal("true", cut.Find(".banner-hero__scrim").GetAttribute("aria-hidden"));
    }
}
