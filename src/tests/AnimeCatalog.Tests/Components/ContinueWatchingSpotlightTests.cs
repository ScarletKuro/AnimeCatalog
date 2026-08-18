using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ContinueWatchingSpotlightTests
{
    [Fact]
    public void Poster_IsRatioLockedAndUsesTheCoverWhenThereIsOne()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry(cover: "https://example.test/cover.jpg")));

        // The figure owns the aspect ratio in CSS; the test pins the structure that relies on it, so
        // the cover can never be sized by the text column beside it again.
        var image = cut.Find(".home-spotlight__poster img");
        Assert.Equal("Frieren cover", image.GetAttribute("alt"));
        Assert.Empty(cut.FindAll(".poster-fallback"));
    }

    [Fact]
    public void MissingCover_RendersLetterFallback()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry(cover: null)));

        Assert.Contains("poster-fallback", cut.Markup);
        Assert.Contains(">F<", cut.Markup);
        Assert.Empty(cut.FindAll(".home-spotlight__poster img"));
    }

    [Fact]
    public void WithoutBanner_NeitherArtworkNorScrimIsRendered()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry()));

        Assert.Empty(cut.FindAll(".home-spotlight__banner"));
        Assert.Empty(cut.FindAll(".home-spotlight__scrim"));
        Assert.DoesNotContain("home-spotlight--banner", cut.Markup);
    }

    [Fact]
    public void WithBanner_ArtworkIsDecorativeAndScrimIsAdded()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry())
            .Add(p => p.BannerUrl, "https://example.test/banner.jpg"));

        // Decorative: the title sits right beside it, so an alt would only add noise.
        Assert.Equal(string.Empty, cut.Find(".home-spotlight__banner").GetAttribute("alt"));
        Assert.Single(cut.FindAll(".home-spotlight__scrim"));
        Assert.Contains("home-spotlight--banner", cut.Markup);
    }

    [Fact]
    public void AiringChip_AppearsOnlyWhenAniListSuppliedIt()
    {
        using var context = new BunitContext();

        var without = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry()));
        Assert.Empty(without.FindAll(".chip--live"));

        var with = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry())
            .Add(p => p.AiringText, "Episode 18 on 2026-08-21 18:30"));
        Assert.Contains("Episode 18 on 2026-08-21 18:30", with.Find(".chip--live").TextContent);
    }

    [Fact]
    public void CommunityScore_IsShownWithItsUnitSoItCannotReadAsOutOfTen()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry())
            .Add(p => p.CommunityScore, 92));

        Assert.Contains("AniList 92 / 100", cut.Markup);
    }

    [Fact]
    public void UnknownEpisodeCount_StatesWhatIsWatchedRatherThanAFakeDenominator()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry(episodes: null, watched: 3)));

        Assert.Equal("3 watched", cut.Find(".stat-meter__track").GetAttribute("aria-valuetext"));
        Assert.DoesNotContain("NaN", cut.Markup);
    }

    [Fact]
    public void KnownEpisodeCount_ReadsAsProgressOutOfTheTotal()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry(episodes: 28, watched: 17)));

        Assert.Equal("17 / 28", cut.Find(".stat-meter__track").GetAttribute("aria-valuetext"));
    }

    [Fact]
    public void FranchiseLink_AppearsOnlyForAGroupedEntry()
    {
        using var context = new BunitContext();

        var loose = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry()));
        Assert.DoesNotContain("Open franchise", loose.Markup);

        var grouped = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry(franchise: new Franchise { Id = 1, Title = "Frieren", Slug = "frieren" })));
        Assert.Contains("Open franchise", grouped.Markup);
        Assert.Contains("franchise/frieren", grouped.Markup);
    }

    [Fact]
    public void ResumeLink_PointsAtTheEntryDetails()
    {
        using var context = new BunitContext();

        var cut = context.Render<ContinueWatchingSpotlight>(parameters => parameters
            .Add(p => p.Item, Entry()));

        Assert.Contains("anime/42", cut.Markup);
    }

    private static AnimeListItemViewModel Entry(
        string? cover = "https://example.test/cover.jpg",
        int? episodes = 28,
        int watched = 17,
        Franchise? franchise = null) =>
        new()
        {
            AnimeEntry = new AnimeEntry
            {
                Id = 42,
                AniListId = 154587,
                TitleRomaji = "Sousou no Frieren",
                TitleEnglish = "Frieren",
                CoverUrl = cover,
                Format = "TV",
                SeasonYear = 2023,
                Episodes = episodes,
                FranchiseId = franchise?.Id
            },
            CatalogEntry = new CatalogEntry
            {
                AnimeEntryId = 42,
                Status = CatalogStatus.Watching,
                Score = 9.0m,
                EpisodesWatched = watched
            },
            Franchise = franchise
        };
}
