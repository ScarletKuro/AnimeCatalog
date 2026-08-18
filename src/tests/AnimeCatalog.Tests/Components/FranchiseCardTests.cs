using AnimeCatalog.Components;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class FranchiseCardTests
{
    [Fact]
    public void ToggleButton_RevealsEntries()
    {
        var visibleEntry = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 1, TitleRomaji = "Sousou no Frieren", TitleEnglish = "Frieren: Beyond Journey's End", Episodes = 28 },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 1, Status = CatalogStatus.Completed, Score = 9.5m }
        };

        var summary = new FranchiseSummaryViewModel
        {
            Title = "Frieren",
            EntryCount = 1,
            CompletedCount = 1,
            Entries = [visibleEntry],
            VisibleEntries = [visibleEntry]
        };

        using var context = new BunitContext();
        var cut = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, summary));

        Assert.DoesNotContain("Frieren: Beyond Journey's End", cut.Markup);
        cut.Find("button").Click();
        Assert.Contains("Frieren: Beyond Journey's End", cut.Markup);
    }

    [Fact]
    public void RatingChip_RendersOnCoverOnlyWhenScored()
    {
        using var context = new BunitContext();

        var scored = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            EntryCount = 3,
            CompletedCount = 2,
            AverageScore = 8.5m
        }));

        Assert.Contains("franchise-card__rating", scored.Markup);
        Assert.Contains("8.5", scored.Markup);

        var unscored = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            EntryCount = 3,
            CompletedCount = 2
        }));

        Assert.DoesNotContain("franchise-card__rating", unscored.Markup);
        Assert.DoesNotContain("Unrated", unscored.Markup);
    }

    [Fact]
    public void Card_WithFranchise_LinksToFranchisePage()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            Slug = "fate",
            EntryCount = 3,
            CompletedCount = 2
        }));

        Assert.Equal("franchise/fate", cut.Find(".franchise-card__link").GetAttribute("href"));
    }

    [Fact]
    public void Card_WithoutFranchise_LinksToAnimePage()
    {
        var standaloneEntry = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 42, TitleRomaji = "Cowboy Bebop", Episodes = 26 },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 42, Status = CatalogStatus.Completed }
        };

        using var context = new BunitContext();

        var cut = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Cowboy Bebop",
            EntryCount = 1,
            CompletedCount = 1,
            Entries = [standaloneEntry],
            VisibleEntries = [standaloneEntry]
        }));

        Assert.Equal("anime/42", cut.Find(".franchise-card__link").GetAttribute("href"));
    }

    [Fact]
    public void EntryCountLabel_NamesTheFranchiseTotalWhenNarrowed()
    {
        var first = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 1, TitleRomaji = "Fate/Zero", Episodes = 25 },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 1, Status = CatalogStatus.Completed }
        };

        var second = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 2, TitleRomaji = "Fate/stay night", Episodes = 24 },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 2, Status = CatalogStatus.Watching }
        };

        using var context = new BunitContext();

        // A filter narrowed five entries down to one: the label promises what expanding reveals, but
        // names the franchise total so the card cannot be read as a one-entry franchise.
        var narrowed = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            EntryCount = 5,
            CompletedCount = 4,
            Entries = [first, second],
            VisibleEntries = [first]
        }));

        // Asserted on the toggle itself: the progress line legitimately says "of N entries" too, so a
        // whole-markup substring check would not be testing the label.
        Assert.Equal("1 of 5 entries", ToggleLabel(narrowed));

        // Nothing is hidden: everything visible means a plain count, not "2 of 2".
        var everythingVisible = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            EntryCount = 2,
            CompletedCount = 1,
            Entries = [first, second],
            VisibleEntries = [first, second]
        }));

        Assert.Equal("2 entries", ToggleLabel(everythingVisible));

        var single = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Fate",
            EntryCount = 1,
            CompletedCount = 1,
            Entries = [first],
            VisibleEntries = [first]
        }));

        Assert.Equal("1 entry", ToggleLabel(single));
    }

    private static string ToggleLabel(Bunit.IRenderedComponent<FranchiseCard> cut) =>
        cut.Find(".franchise-card__toggle span").TextContent.Trim();

    [Fact]
    public void Progress_ReportsTheWholeFranchiseEvenWhenAFilterHidesEntries()
    {
        var completed = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 343, TitleRomaji = "Classroom of the Elite" },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 343, Status = CatalogStatus.Completed }
        };

        var watching = new AnimeListItemViewModel
        {
            AnimeEntry = new AnimeEntry { Id = 346, TitleRomaji = "Classroom of the Elite 4th Season" },
            CatalogEntry = new CatalogEntry { AnimeEntryId = 346, Status = CatalogStatus.Watching }
        };

        using var context = new BunitContext();

        // The reported case: a Watching filter leaves only the unfinished season visible. Counting
        // just that row used to render "0 completed", implying none of the franchise was finished.
        var cut = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Classroom of the Elite",
            EntryCount = 4,
            CompletedCount = 3,
            Entries = [completed, watching],
            VisibleEntries = [watching]
        }));

        Assert.Contains("3 / 4 completed", cut.Markup);
        Assert.DoesNotContain("0 completed", cut.Markup);
        Assert.Equal("1 of 4 entries", ToggleLabel(cut));
    }

    [Fact]
    public void Progress_IsSpelledOutForScreenReaders()
    {
        using var context = new BunitContext();

        var cut = context.Render<FranchiseCard>(parameters => parameters.Add(p => p.Summary, new FranchiseSummaryViewModel
        {
            Title = "Classroom of the Elite",
            EntryCount = 4,
            CompletedCount = 3
        }));

        // "3 / 4" is announced as "3 slash 4", so the visible form is hidden and a spoken form added.
        Assert.Equal("true", cut.Find(".franchise-card__meta span").GetAttribute("aria-hidden"));
        Assert.Contains("3 of 4 entries completed", cut.Find(".franchise-card__meta .sr-only").TextContent);
    }
}
