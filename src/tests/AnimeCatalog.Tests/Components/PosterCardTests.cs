using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class PosterCardTests
{
    [Fact]
    public void MissingCover_RendersLetterFallback()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "frieren"));

        Assert.Contains("poster-fallback", cut.Markup);
        Assert.Contains(">F<", cut.Markup);
        Assert.Empty(cut.FindAll(".poster-card__figure img"));
    }

    [Fact]
    public void Cover_RendersImageWithDescriptiveAlt()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.CoverUrl, "https://example.test/cover.jpg"));

        Assert.Equal("Frieren cover", cut.Find(".poster-card__figure img").GetAttribute("alt"));
        Assert.Empty(cut.FindAll(".poster-fallback"));
    }

    [Fact]
    public void ScoreChip_RendersOnlyWhenScored()
    {
        using var context = new BunitContext();

        var scored = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Score, 9.5m));

        Assert.Contains("poster-card__score", scored.Markup);
        Assert.Contains("9.5", scored.Markup);

        var unscored = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.DoesNotContain("poster-card__score", unscored.Markup);
        Assert.DoesNotContain("Unrated", unscored.Markup);
    }

    [Fact]
    public void InternalHref_RendersLocalLinkWithoutNewTabAttributes()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Href, "anime/42"));

        var link = cut.Find(".poster-card__link");
        Assert.Equal("anime/42", link.GetAttribute("href"));
        Assert.Null(link.GetAttribute("target"));
    }

    [Fact]
    public void ExternalHref_OpensNewTabSafely()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "One Piece")
            .Add(p => p.Href, "https://anilist.co/anime/21")
            .Add(p => p.IsExternal, true));

        var link = cut.Find(".poster-card__link");
        Assert.Equal("_blank", link.GetAttribute("target"));

        var rel = link.GetAttribute("rel");
        Assert.Contains("noopener", rel);
        Assert.Contains("noreferrer", rel);
        Assert.Contains("new tab", link.GetAttribute("aria-label"));
        Assert.Contains("poster-card--external", cut.Markup);
    }

    [Fact]
    public void NoHref_RendersNoLink()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(cut.FindAll(".poster-card__link"));
    }

    [Theory]
    [InlineData(150, "--progress:100%")]
    [InlineData(-10, "--progress:0%")]
    [InlineData(50, "--progress:50%")]
    public void ProgressPercent_IsClampedIntoCssVariable(int value, string expected)
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.ProgressPercent, value));

        Assert.Contains(expected, cut.Markup);
    }

    [Fact]
    public void NoProgress_RendersNoProgressBar()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(cut.FindAll(".poster-card__progress"));
    }

    [Fact]
    public void BadgeAndScore_ShareOneRowSoTheyCannotOverlap()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Fate/Zero")
            .Add(p => p.BadgeText, "Watching")
            .Add(p => p.BadgeVariant, "live")
            .Add(p => p.Score, 9m));

        var top = cut.Find(".poster-card__top");
        Assert.NotNull(top.QuerySelector(".poster-card__badge"));
        Assert.NotNull(top.QuerySelector(".poster-card__score"));
    }

    [Fact]
    public void TopRow_IsOmittedWhenThereIsNoBadgeOrScore()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Empty(cut.FindAll(".poster-card__top"));
    }

    [Fact]
    public void LongRelationLabel_RendersInFullWithoutTruncation()
    {
        using var context = new BunitContext();

        // Long labels used to be clipped by a nowrap pill with a gutter reserved for a score chip
        // that relation cards never render.
        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Rotte no Omocha!")
            .Add(p => p.BadgeText, "Alternative version"));

        Assert.Equal("Alternative version", cut.Find(".poster-card__badge").TextContent.Trim());
    }

    [Fact]
    public void Badge_RendersWithRequestedVariant()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.BadgeText, "Sequel")
            .Add(p => p.BadgeVariant, "warm"));

        Assert.Contains("poster-card__badge--warm", cut.Markup);
        Assert.Contains("Sequel", cut.Markup);
    }

    [Fact]
    public void Title_IsHeadingLevelThree()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.Equal("H3", cut.Find(".poster-card__title").TagName);
    }

    [Fact]
    public void Footer_RendersSuppliedContent()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.Footer, builder => builder.AddMarkupContent(0, "<span>28 eps</span>")));

        Assert.Contains("28 eps", cut.Find(".poster-card__footer").InnerHtml);
    }

    // Guards the property every existing caller relies on: adding the archive's highlight ring must
    // leave the markup of a card that does not ask for it completely unchanged.
    [Fact]
    public void Highlight_IsOptIn_AndDefaultsOff()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren"));

        Assert.DoesNotContain("poster-card--highlighted", cut.Find(".poster-card").ClassList);
    }

    [Fact]
    public void Highlight_RingsTheTileWhenAskedFor()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.IsHighlighted, true));

        Assert.Contains("poster-card--highlighted", cut.Find(".poster-card").ClassList);
    }

    // Highlighted and dimmed are opposites, but they are independent flags and the archive sets them
    // from different inputs, so neither may swallow the other.
    [Fact]
    public void HighlightAndDim_AreIndependent()
    {
        using var context = new BunitContext();

        var cut = context.Render<PosterCard>(parameters => parameters
            .Add(p => p.Title, "Frieren")
            .Add(p => p.IsHighlighted, true)
            .Add(p => p.IsDimmed, true));

        var classes = cut.Find(".poster-card").ClassList;

        Assert.Contains("poster-card--highlighted", classes);
        Assert.Contains("poster-card--dimmed", classes);
    }
}
