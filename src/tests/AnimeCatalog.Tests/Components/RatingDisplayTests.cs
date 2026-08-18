using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class RatingDisplayTests
{
    [Fact]
    public void Default_RendersTheFullUnratedWordAndNothingElse()
    {
        using var context = new BunitContext();
        var cut = context.Render<RatingDisplay>();

        // UnratedLabel exists for the franchise card's narrow score column. Every other caller
        // (AnimeDetails, PosterCard, ContinueWatchingSpotlight) must keep the markup it had before
        // the parameter existed -- no aria-hidden on the value, no screen-reader duplicate. The
        // star's own svg is aria-hidden and always has been, so the selector skips it.
        Assert.Contains("Unrated", cut.Markup);
        Assert.Empty(cut.FindAll(".sr-only"));
        Assert.Empty(cut.FindAll("span[aria-hidden]"));
    }

    [Fact]
    public void AbbreviatedUnrated_HidesTheGlyphFromScreenReadersAndSpellsItOut()
    {
        using var context = new BunitContext();
        var cut = context.Render<RatingDisplay>(parameters => parameters.Add(p => p.UnratedLabel, "–"));

        Assert.Equal("Unrated", cut.Find(".sr-only").TextContent);
        Assert.Equal("–", cut.Find("span[aria-hidden=\"true\"]").TextContent);
    }

    [Fact]
    public void ScoredEntry_IgnoresTheUnratedLabelEntirely()
    {
        using var context = new BunitContext();
        var cut = context.Render<RatingDisplay>(parameters => parameters
            .Add(p => p.Score, 9.5m)
            .Add(p => p.UnratedLabel, "–"));

        Assert.Contains("9.5", cut.Markup);
        Assert.Empty(cut.FindAll(".sr-only"));
    }
}
