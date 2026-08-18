using AnimeCatalog.Components;
using AnimeCatalog.Models.AniList;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class AnimeSearchResultTests
{
    [Fact]
    public void RendersTitleMetadataAndSelectedState()
    {
        var media = new AniListMedia
        {
            Id = 198113,
            Title = new AniListTitle
            {
                English = "KILL BLUE",
                Romaji = "Kill Ao"
            },
            Format = "TV",
            SeasonYear = 2026,
            CoverImage = new AniListCoverImage
            {
                Large = "https://example.invalid/kill-blue.jpg"
            }
        };

        using var context = new BunitContext();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters
            .Add(p => p.Media, media)
            .Add(p => p.IsSelected, true));

        Assert.Contains("KILL BLUE", cut.Markup);
        Assert.Contains("Kill Ao", cut.Markup);
        Assert.Contains("TV", cut.Markup);
        Assert.Contains("2026", cut.Markup);
        Assert.Contains("search-result--selected", cut.Markup);
    }

    [Fact]
    public void FallsBackToRomajiWithoutDuplicateSubtitle()
    {
        var media = new AniListMedia
        {
            Id = 1,
            Title = new AniListTitle
            {
                Romaji = "Bleach"
            },
            Format = "TV"
        };

        using var context = new BunitContext();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters.Add(p => p.Media, media));

        Assert.Contains("Bleach", cut.Markup);
        Assert.DoesNotContain("search-result__subtitle", cut.Markup);
    }

    [Fact]
    public void MarksResultAlreadyInTheCatalog()
    {
        var media = new AniListMedia
        {
            Id = 21,
            Title = new AniListTitle { English = "One Piece" },
            Format = "TV"
        };

        using var context = new BunitContext();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters
            .Add(p => p.Media, media)
            .Add(p => p.ExistingAnimeEntryId, 7L));

        Assert.Contains("In catalog", cut.Markup);
        Assert.Contains("search-result--cataloged", cut.Markup);
    }

    [Fact]
    public void LeavesResultUnmarkedWhenNotInTheCatalog()
    {
        var media = new AniListMedia
        {
            Id = 21,
            Title = new AniListTitle { English = "One Piece" },
            Format = "TV"
        };

        using var context = new BunitContext();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters.Add(p => p.Media, media));

        Assert.DoesNotContain("In catalog", cut.Markup);
        Assert.DoesNotContain("search-result--cataloged", cut.Markup);
    }

    [Fact]
    public void KeepsAlreadyAddedResultSelectable()
    {
        var media = new AniListMedia
        {
            Id = 21,
            Title = new AniListTitle { English = "One Piece" },
            Format = "TV"
        };

        using var context = new BunitContext();

        var selectedIds = new List<int>();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters
            .Add(p => p.Media, media)
            .Add(p => p.ExistingAnimeEntryId, 7L)
            .Add(p => p.OnSelected, m => selectedIds.Add(m.Id)));

        cut.Find(".search-result").Click();

        Assert.Equal([21], selectedIds);
    }

    [Fact]
    public void CombinesCatalogedAndSelectedModifiers()
    {
        var media = new AniListMedia
        {
            Id = 21,
            Title = new AniListTitle { English = "One Piece" },
            Format = "TV"
        };

        using var context = new BunitContext();
        var cut = context.Render<AnimeSearchResult>(parameters => parameters
            .Add(p => p.Media, media)
            .Add(p => p.ExistingAnimeEntryId, 7L)
            .Add(p => p.IsSelected, true));

        var classes = cut.Find(".search-result").ClassList;

        Assert.Contains("search-result--selected", classes);
        Assert.Contains("search-result--cataloged", classes);
    }
}
