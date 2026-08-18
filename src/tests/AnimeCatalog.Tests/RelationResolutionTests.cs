using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

public sealed class RelationResolutionTests
{
    private readonly FranchiseService _service = new();

    // ---- Confirmation: only non-music anime may render ----------------------

    [Fact]
    public void ResolveRelations_ConfirmsAnInCatalogTargetWithoutAnyAniListData()
    {
        // anime_entries only holds anime, so an in-catalog target needs no classification. This is
        // also the AniList-down path: these relations must still render.
        var relations = new[] { Relation(1, targetAniListId: 999, "SEQUEL") };
        var entries = new[] { AnimeEntry(id: 42, aniListId: 999, english: "Fate/Zero") };
        var catalog = new[] { Catalog(42, CatalogStatus.Completed) };

        var resolved = Assert.Single(_service.ResolveRelations(relations, null, entries, catalog));

        Assert.True(resolved.IsConfirmedAnime);
        Assert.True(resolved.IsInCatalog);
        Assert.Equal(42, resolved.LocalAnimeEntryId);
        Assert.Equal("anime/42", resolved.Href);
        Assert.Equal("Fate/Zero", resolved.Title);
        Assert.Equal(CatalogStatus.Completed, resolved.CatalogStatus);
    }

    [Fact]
    public void ResolveRelations_LeavesAnOutOfCatalogTargetUnconfirmedUntilAniListClassifiesIt()
    {
        var relations = new[] { Relation(1, targetAniListId: 999, "SEQUEL") };

        var resolved = Assert.Single(_service.ResolveRelations(relations, null, [], []));

        // Kept so enrichment can resolve it and the page can report progress, but not renderable:
        // without a type it could still turn out to be a manga.
        Assert.False(resolved.IsConfirmedAnime);
        Assert.False(resolved.IsInCatalog);
    }

    [Fact]
    public void ResolveRelations_ConfirmsAndFillsAnOutOfCatalogAnime()
    {
        var relations = new[] { Relation(1, targetAniListId: 999, "SEQUEL") };
        var live = Live(Node(999, "ANIME", title: "Heaven's Feel", format: "MOVIE", seasonYear: 2017,
            cover: "cover.jpg", siteUrl: "https://anilist.co/anime/999"));

        var resolved = Assert.Single(_service.ResolveRelations(relations, live, [], []));

        Assert.True(resolved.IsConfirmedAnime);
        Assert.Equal("Heaven's Feel", resolved.Title);
        Assert.Equal("cover.jpg", resolved.CoverUrl);
        Assert.Equal("MOVIE", resolved.Format);
        Assert.Equal(2017, resolved.SeasonYear);
        Assert.Equal("https://anilist.co/anime/999", resolved.Href);
    }

    [Fact]
    public void ResolveRelations_DropsAMangaTarget()
    {
        // The largest real-world bucket: ADAPTATION targets are the source manga, and this catalog
        // holds no manga. The anime page's Source row already states the adaptation.
        var relations = new[] { Relation(1, targetAniListId: 36778, "ADAPTATION") };
        var live = Live(Node(36778, "MANGA", title: "Rotte no Omocha!", format: "MANGA"));

        Assert.Empty(_service.ResolveRelations(relations, live, [], []));
    }

    [Fact]
    public void ResolveRelations_DropsAMusicFormatAnime()
    {
        // Theme songs and AMVs are type ANIME, so type alone would let them through.
        var relations = new[] { Relation(1, targetAniListId: 500, "SIDE_STORY") };
        var live = Live(Node(500, "ANIME", title: "Opening Theme", format: "MUSIC"));

        Assert.Empty(_service.ResolveRelations(relations, live, [], []));
    }

    [Fact]
    public void ResolveRelations_DropsAnInCatalogMusicEntry()
    {
        var relations = new[] { Relation(1, targetAniListId: 999, "SIDE_STORY") };
        var entries = new[] { AnimeEntry(id: 42, aniListId: 999, english: "Ending Theme", format: "MUSIC") };

        Assert.Empty(_service.ResolveRelations(relations, null, entries, []));
    }

    [Theory]
    [InlineData("CHARACTER")]
    [InlineData("OTHER")]
    public void ResolveRelations_DropsWeakRelationTypesEvenWhenTheTargetIsInTheCatalog(string relationType)
    {
        var relations = new[] { Relation(1, targetAniListId: 999, relationType) };
        var entries = new[] { AnimeEntry(id: 42, aniListId: 999, english: "Cameo appearance") };
        var catalog = new[] { Catalog(42, CatalogStatus.Completed) };

        Assert.Empty(_service.ResolveRelations(relations, null, entries, catalog));
    }

    [Fact]
    public void ResolveRelations_DropsANodeAniListReturnedWithoutAType()
    {
        // Fail-safe: never assume anime. Dropping rather than holding it as "unresolved" is
        // deliberate — a node that came back with no type will not gain one on a retry, so keeping it
        // would inflate the unresolved count forever.
        var relations = new[] { Relation(1, targetAniListId: 999, "SEQUEL") };
        var live = Live(Node(999, type: null, title: "Unknown kind"));

        Assert.Empty(_service.ResolveRelations(relations, live, [], []));
    }

    // ---- Shaping -----------------------------------------------------------

    [Fact]
    public void ResolveRelations_PrefersTheLocalTitleOverTheLiveOne()
    {
        var relations = new[] { Relation(1, targetAniListId: 999, "SEQUEL") };
        var entries = new[] { AnimeEntry(id: 42, aniListId: 999, english: "Local title") };
        var live = Live(Node(999, "ANIME", title: "Live title"));

        var resolved = Assert.Single(_service.ResolveRelations(relations, live, entries, []));

        Assert.Equal("Local title", resolved.Title);
    }

    [Fact]
    public void ResolveRelations_OrdersBySourceThenPrequelThenSequel()
    {
        var relations = new[]
        {
            Relation(1, 3, "SEQUEL"),
            Relation(1, 2, "PREQUEL"),
            Relation(1, 1, "SOURCE"),
            Relation(1, 4, "SPIN_OFF")
        };

        var entries = new[]
        {
            AnimeEntry(1, 1, "Source"),
            AnimeEntry(2, 2, "Prequel"),
            AnimeEntry(3, 3, "Sequel"),
            AnimeEntry(4, 4, "Spin-off")
        };

        var resolved = _service.ResolveRelations(relations, null, entries, []);

        Assert.Equal(["SOURCE", "PREQUEL", "SEQUEL", "SPIN_OFF"], resolved.Select(item => item.RelationType));
    }

    [Fact]
    public void ResolveRelations_DeduplicatesIdenticalTargetAndType()
    {
        var relations = new[] { Relation(1, 999, "SEQUEL"), Relation(1, 999, "SEQUEL") };
        var entries = new[] { AnimeEntry(42, 999, "Fate/Zero") };

        Assert.Single(_service.ResolveRelations(relations, null, entries, []));
    }

    [Fact]
    public void ResolveRelations_KeepsTheSameTargetUnderTwoDifferentRelationTypes()
    {
        var relations = new[] { Relation(1, 999, "SEQUEL"), Relation(1, 999, "SIDE_STORY") };
        var entries = new[] { AnimeEntry(42, 999, "Fate/Zero") };

        Assert.Equal(2, _service.ResolveRelations(relations, null, entries, []).Count);
    }

    [Fact]
    public void ResolveRelations_ToleratesDuplicateAniListIdsInTheSnapshot()
    {
        // anilist_id is unique in the schema, but the method must stay total for arbitrary input.
        var relations = new[] { Relation(1, 999, "SEQUEL") };
        var entries = new[]
        {
            AnimeEntry(id: 42, aniListId: 999, english: "First"),
            AnimeEntry(id: 43, aniListId: 999, english: "Second")
        };

        var resolved = Assert.Single(_service.ResolveRelations(relations, null, entries, []));

        Assert.Equal(42, resolved.LocalAnimeEntryId);
    }

    [Fact]
    public void ResolveRelations_WithNoRowsReturnsEmpty()
    {
        Assert.Empty(_service.ResolveRelations([], null, [], []));
    }

    [Fact]
    public void DisplayLabel_HumanizesTheRelationType()
    {
        var relations = new[] { Relation(1, 999, "SIDE_STORY") };
        var entries = new[] { AnimeEntry(42, 999, "Fate/Zero") };

        var resolved = Assert.Single(_service.ResolveRelations(relations, null, entries, []));

        Assert.Equal("Side story", resolved.DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_FallsBackToTitleCaseForAnUnknownRelationType()
    {
        // AniList can add MediaRelation members at any time; this must not throw.
        var relations = new[] { Relation(1, 999, "SOME_NEW_KIND") };
        var entries = new[] { AnimeEntry(42, 999, "Fate/Zero") };

        var resolved = Assert.Single(_service.ResolveRelations(relations, null, entries, []));

        Assert.Equal("Some new kind", resolved.DisplayLabel);
    }

    // ---- MergeLiveRelationData --------------------------------------------

    [Fact]
    public void MergeLiveRelationData_ConfirmsAndFillsAnOutOfCatalogAnime()
    {
        var relations = new[] { Relation(1, 222, "PREQUEL") };
        var resolved = _service.ResolveRelations(relations, null, [], []);
        Assert.False(Assert.Single(resolved).IsConfirmedAnime);

        var live = Live(Node(222, "ANIME", title: "Live outside", format: "TV", seasonYear: 2015));

        var merged = Assert.Single(_service.MergeLiveRelationData(resolved, live));

        Assert.True(merged.IsConfirmedAnime);
        Assert.Equal("Live outside", merged.Title);
        Assert.Equal("TV", merged.Format);
        Assert.Equal(2015, merged.SeasonYear);
    }

    [Fact]
    public void MergeLiveRelationData_DropsATargetThatTurnsOutToBeManga()
    {
        var relations = new[] { Relation(1, 36778, "ADAPTATION") };
        var resolved = _service.ResolveRelations(relations, null, [], []);

        var live = Live(Node(36778, "MANGA", title: "Rotte no Omocha!", format: "MANGA"));

        Assert.Empty(_service.MergeLiveRelationData(resolved, live));
    }

    [Fact]
    public void MergeLiveRelationData_DropsATargetThatTurnsOutToBeMusic()
    {
        var relations = new[] { Relation(1, 500, "SIDE_STORY") };
        var resolved = _service.ResolveRelations(relations, null, [], []);

        var live = Live(Node(500, "ANIME", title: "Opening Theme", format: "MUSIC"));

        Assert.Empty(_service.MergeLiveRelationData(resolved, live));
    }

    [Fact]
    public void MergeLiveRelationData_LeavesInCatalogRowsUntouched()
    {
        var relations = new[] { Relation(1, 111, "SEQUEL") };
        var entries = new[] { AnimeEntry(id: 42, aniListId: 111, english: "In catalog") };
        var resolved = _service.ResolveRelations(relations, null, entries, []);

        var live = Live(Node(111, "ANIME", title: "Live in-catalog"));

        var merged = Assert.Single(_service.MergeLiveRelationData(resolved, live));

        Assert.Equal("In catalog", merged.Title);
        Assert.Equal(42, merged.LocalAnimeEntryId);
    }

    [Fact]
    public void MergeLiveRelationData_PoolsEdgesFromSeveralMedia()
    {
        // The franchise page classifies its out-of-catalog targets from every entry's edges at once.
        var relations = new[] { Relation(1, 111, "SEQUEL"), Relation(2, 222, "PREQUEL") };
        var resolved = _service.ResolveRelations(relations, null, [], []);

        var first = Live(Node(111, "ANIME", title: "From entry one"));
        var second = Live(Node(222, "ANIME", title: "From entry two"));

        var merged = _service.MergeLiveRelationData(resolved, new[] { first, second });

        Assert.Equal(2, merged.Count);
        Assert.All(merged, relation => Assert.True(relation.IsConfirmedAnime));
        Assert.Contains(merged, relation => relation.Title == "From entry one");
        Assert.Contains(merged, relation => relation.Title == "From entry two");
    }

    [Fact]
    public void MergeLiveRelationData_WithNoLiveEdgesReturnsTheInputUnchanged()
    {
        var relations = new[] { Relation(1, 999, "SEQUEL") };
        var resolved = _service.ResolveRelations(relations, null, [], []);

        Assert.Same(resolved, _service.MergeLiveRelationData(resolved, new AniListMedia { Id = 1 }));
    }

    // ---- ResolveRelatedOutsideFranchise -----------------------------------

    [Fact]
    public void ResolveRelatedOutsideFranchise_ExcludesEntriesAlreadyInTheFranchise()
    {
        var inside = FranchiseEntry(
            AnimeEntry(id: 1, aniListId: 100, english: "Season 1"),
            Relation(1, 200, "SEQUEL"),      // also in the franchise
            Relation(1, 300, "SIDE_STORY")); // outside, and in the catalog

        var sibling = FranchiseEntry(AnimeEntry(id: 2, aniListId: 200, english: "Season 2"));

        var catalogued = new[]
        {
            AnimeEntry(id: 1, aniListId: 100, english: "Season 1"),
            AnimeEntry(id: 2, aniListId: 200, english: "Season 2"),
            AnimeEntry(id: 3, aniListId: 300, english: "Spin-off")
        };

        var outside = _service.ResolveRelatedOutsideFranchise([inside, sibling], catalogued, []);

        var single = Assert.Single(outside);
        Assert.Equal(300, single.TargetAniListId);
        Assert.True(single.IsConfirmedAnime);
    }

    [Fact]
    public void ResolveRelatedOutsideFranchise_LeavesOutOfCatalogTargetsForEnrichment()
    {
        var inside = FranchiseEntry(
            AnimeEntry(id: 1, aniListId: 100, english: "Season 1"),
            Relation(1, 900, "SEQUEL"));

        var outside = Assert.Single(_service.ResolveRelatedOutsideFranchise([inside], [], []));

        Assert.False(outside.IsConfirmedAnime);

        // Once AniList says it is an anime, it becomes renderable.
        var merged = Assert.Single(_service.MergeLiveRelationData(
            outside is null ? [] : new[] { outside },
            Live(Node(900, "ANIME", title: "Unlisted sequel"))));

        Assert.True(merged.IsConfirmedAnime);
        Assert.Equal("Unlisted sequel", merged.Title);
    }

    [Fact]
    public void ResolveRelatedOutsideFranchise_DeduplicatesTargetsSharedBySeveralEntries()
    {
        var first = FranchiseEntry(
            AnimeEntry(id: 1, aniListId: 100, english: "Season 1"),
            Relation(1, 900, "SIDE_STORY"));

        var second = FranchiseEntry(
            AnimeEntry(id: 2, aniListId: 200, english: "Season 2"),
            Relation(2, 900, "SIDE_STORY"));

        Assert.Single(_service.ResolveRelatedOutsideFranchise([first, second], [], []));
    }

    [Fact]
    public void ResolveRelatedOutsideFranchise_WithNoEntriesReturnsEmpty()
    {
        Assert.Empty(_service.ResolveRelatedOutsideFranchise([], [], []));
    }

    // ---- Franchise siblings -----------------------------------------------

    [Fact]
    public void BuildAnimeDetails_SurfacesASecondSeasonAniListOnlyLinksThroughAMovie()
    {
        // The real case: AniList links Made in Abyss S1 to the movie, and only the movie links on to
        // S2. One hop of relations can never reach S2, but the franchise grouping does.
        var franchise = new Franchise { Id = 137, Title = "Made in Abyss", Slug = "made-in-abyss" };

        var season1 = Franchised(AnimeEntry(469, 97986, "Made in Abyss"), franchise.Id);
        var season2 = Franchised(AnimeEntry(470, 114745, "Made in Abyss: The Golden City"), franchise.Id);

        var details = _service.BuildAnimeDetails(
            season1,
            Catalog(469, CatalogStatus.Completed),
            // S1's only anime relation is the movie, which is not in the catalog.
            [Relation(469, 100643, "SEQUEL")],
            franchise,
            [season1, season2],
            [Catalog(469, CatalogStatus.Completed), Catalog(470, CatalogStatus.Watching)]);

        var sibling = Assert.Single(details.Relations, relation => relation.LocalAnimeEntryId == 470);

        Assert.Equal("Same franchise", sibling.DisplayLabel);
        Assert.True(sibling.IsConfirmedAnime);
        Assert.Equal("anime/470", sibling.Href);
        Assert.Equal(CatalogStatus.Watching, sibling.CatalogStatus);

        // Curated siblings read first; the unresolved movie is still carried for enrichment.
        Assert.Equal(470, details.Relations[0].LocalAnimeEntryId);
        Assert.Contains(details.Relations, relation => relation.TargetAniListId == 100643);
    }

    [Fact]
    public void AppendFranchiseSiblings_ExcludesTheAnimeItself()
    {
        var franchise = new Franchise { Id = 1, Title = "Fate", Slug = "fate" };
        var self = Franchised(AnimeEntry(10, 100, "Fate/Zero"), franchise.Id);

        Assert.Empty(_service.AppendFranchiseSiblings([], self, franchise, [self], []));
    }

    [Fact]
    public void AppendFranchiseSiblings_KeepsTheAniListLabelWhenTheSiblingIsAlreadyRelated()
    {
        // Astarotte's OVA is both a franchise sibling and a SIDE_STORY. "Side story" is the more
        // informative label, and it must not appear twice.
        var franchise = new Franchise { Id = 65, Title = "Astarotte", Slug = "astarottes-toy" };
        var main = Franchised(AnimeEntry(196, 9736, "Astarotte's Toy"), franchise.Id);
        var ova = Franchised(AnimeEntry(197, 10582, "Astarotte's Toy EX"), franchise.Id);

        var details = _service.BuildAnimeDetails(
            main,
            Catalog(196, CatalogStatus.Completed),
            [Relation(196, 10582, "SIDE_STORY")],
            franchise,
            [main, ova],
            [Catalog(196, CatalogStatus.Completed), Catalog(197, CatalogStatus.Completed)]);

        var single = Assert.Single(details.Relations);
        Assert.Equal("Side story", single.DisplayLabel);
        Assert.Equal(197, single.LocalAnimeEntryId);
    }

    [Fact]
    public void AppendFranchiseSiblings_WithoutAFranchiseChangesNothing()
    {
        var self = AnimeEntry(10, 100, "Cowboy Bebop");
        var other = Franchised(AnimeEntry(11, 101, "Something else"), franchiseId: 5);

        Assert.Empty(_service.AppendFranchiseSiblings([], self, franchise: null, [self, other], []));
    }

    [Fact]
    public void AppendFranchiseSiblings_SkipsMusicEntries()
    {
        var franchise = new Franchise { Id = 1, Title = "Fate", Slug = "fate" };
        var self = Franchised(AnimeEntry(10, 100, "Fate/Zero"), franchise.Id);
        var theme = Franchised(AnimeEntry(11, 101, "Fate/Zero OP", format: "MUSIC"), franchise.Id);

        Assert.Empty(_service.AppendFranchiseSiblings([], self, franchise, [self, theme], []));
    }

    [Fact]
    public void AppendFranchiseSiblings_IgnoresEntriesFromOtherFranchises()
    {
        var franchise = new Franchise { Id = 1, Title = "Fate", Slug = "fate" };
        var self = Franchised(AnimeEntry(10, 100, "Fate/Zero"), franchise.Id);
        var stranger = Franchised(AnimeEntry(11, 101, "Unrelated"), franchiseId: 99);

        Assert.Empty(_service.AppendFranchiseSiblings([], self, franchise, [self, stranger], []));
    }

    // ---- Fixtures ---------------------------------------------------------

    private static AnimeEntry Franchised(AnimeEntry entry, long franchiseId)
    {
        entry.FranchiseId = franchiseId;
        return entry;
    }

    private static ViewModels.AnimeListItemViewModel FranchiseEntry(AnimeEntry entry, params AnimeRelation[] relations) => new()
    {
        AnimeEntry = entry,
        CatalogEntry = Catalog(entry.Id, CatalogStatus.Completed),
        Relations = relations
    };

    private static AniListMedia Node(
        int id,
        string? type = "ANIME",
        string? title = null,
        string? format = null,
        int? seasonYear = null,
        string? cover = null,
        string? siteUrl = null) => new()
        {
            Id = id,
            Type = type,
            Title = new AniListTitle { English = title },
            Format = format,
            SeasonYear = seasonYear,
            CoverImage = cover is null ? null : new AniListCoverImage { ExtraLarge = cover },
            SiteUrl = siteUrl
        };

    private static AniListMedia Live(params AniListMedia[] nodes) => new()
    {
        Id = 1,
        Relations = new AniListRelationConnection
        {
            Edges = nodes.Select(node => new AniListRelationEdge { RelationType = "SEQUEL", Node = node }).ToList()
        }
    };

    private static AnimeRelation Relation(long sourceId, int targetAniListId, string relationType) => new()
    {
        SourceAnimeId = sourceId,
        TargetAniListId = targetAniListId,
        RelationType = relationType
    };

    private static AnimeEntry AnimeEntry(long id, int aniListId, string english, string? format = null) => new()
    {
        Id = id,
        AniListId = aniListId,
        TitleRomaji = english,
        TitleEnglish = english,
        Format = format
    };

    private static CatalogEntry Catalog(long animeEntryId, CatalogStatus status) => new()
    {
        AnimeEntryId = animeEntryId,
        Status = status
    };
}
