using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class FranchiseGapServiceTests
{
    [Fact]
    public async Task ScanAsync_ReachesASeasonThreeHopsFromWhatYouWatched()
    {
        // The real Darker than Black graph. Season one links to a special, which links to another
        // special, which links to season two — so anything shallower than a full walk misses it.
        var graph = new FakeEnrichmentService(
            Anime(2025, "Darker than Black", score: 77, popularity: 90_000, ("SEQUEL", 4182)),
            Anime(4182, "Sakura no Hana", score: 72, edges: [("SEQUEL", 7338), ("PREQUEL", 2025)]),
            Anime(7338, "Gaiden", score: 76, edges: [("SEQUEL", 6573), ("PREQUEL", 4182)]),
            Anime(6573, "Ryuusei no Gemini", score: 71, edges: [("PREQUEL", 7338)]));

        var scan = await Scan(graph, Owned(2025, CatalogStatus.Completed));

        var group = Assert.Single(scan.Groups);
        Assert.Equal(1, group.OwnedCount);
        Assert.Equal(4, group.TotalCount);

        Assert.Equal(
            ["Gaiden", "Sakura no Hana", "Ryuusei no Gemini"],
            group.Missing.Select(item => item.Title));

        // Season two is the whole point: three hops out, and explained by how it was reached. The edge
        // is Gaiden's SEQUEL, so it reads "Sequel · via Gaiden" rather than naming season one, which
        // it is not directly related to.
        var seasonTwo = group.Missing.Single(item => item.AniListId == 6573);
        Assert.Equal("Sequel", seasonTwo.DisplayLabel);
        Assert.Equal("Gaiden", seasonTwo.DiscoveredFrom);
    }

    [Fact]
    public async Task ScanAsync_ExcludesEverythingAlreadyInTheCatalogWhateverItsStatus()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SEQUEL", 2)]),
            Anime(2, "Season 2", score: 82, edges: [("SEQUEL", 3), ("PREQUEL", 1)]),
            Anime(3, "Season 3", score: 84, edges: [("PREQUEL", 2)]));

        // Season 2 is only Planned — untouched, but tracked, so it is not "missing".
        var scan = await Scan(graph, [
            .. Owned(1, CatalogStatus.Completed),
            .. Owned(2, CatalogStatus.Planned)
        ]);

        var group = Assert.Single(scan.Groups);
        Assert.Equal(3, Assert.Single(group.Missing).AniListId);
        Assert.Equal(2, group.OwnedCount);
    }

    [Theory]
    [InlineData(CatalogStatus.Completed, true)]
    [InlineData(CatalogStatus.Watching, true)]
    [InlineData(CatalogStatus.Planned, false)]
    [InlineData(CatalogStatus.Dropped, false)]
    [InlineData(CatalogStatus.OnHold, false)]
    public async Task ScanAsync_OnlyFinishedAndInProgressEntriesSeedTheWalk(CatalogStatus status, bool expectResults)
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SEQUEL", 2)]),
            Anime(2, "Season 2", score: 82, edges: [("PREQUEL", 1)]));

        var scan = await Scan(graph, Owned(1, status));

        Assert.Equal(expectResults, scan.Groups.Count > 0);
    }

    [Fact]
    public async Task ScanAsync_DoesNotTraverseCharacterEdgesSoUnrelatedFranchisesStaySeparate()
    {
        // A crossover cameo must not fuse two franchises: following it would merge unrelated clusters
        // and send the walk across a large part of AniList.
        var graph = new FakeEnrichmentService(
            Anime(1, "Franchise A", score: 80, edges: [("SEQUEL", 2), ("CHARACTER", 10)]),
            Anime(2, "A Season 2", score: 82, edges: [("PREQUEL", 1)]),
            Anime(10, "Franchise B", score: 88, edges: [("CHARACTER", 1), ("SEQUEL", 11)]),
            Anime(11, "B Season 2", score: 90, edges: [("PREQUEL", 10)]));

        var scan = await Scan(graph, Owned(1, CatalogStatus.Completed));

        var group = Assert.Single(scan.Groups);
        Assert.Equal(2, Assert.Single(group.Missing).AniListId);
        Assert.DoesNotContain(scan.Groups.SelectMany(item => item.Missing), item => item.AniListId == 11);
        Assert.DoesNotContain(graph.RequestedIds, id => id == 10);
    }

    [Fact]
    public async Task ScanAsync_NeverFetchesTheSourceManga()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SEQUEL", 2), ("ADAPTATION", 900), ("SOURCE", 901)]),
            Anime(2, "Season 2", score: 82, edges: [("PREQUEL", 1)]));

        var scan = await Scan(graph, Owned(1, CatalogStatus.Completed));

        Assert.Equal(2, Assert.Single(Assert.Single(scan.Groups).Missing).AniListId);
        Assert.DoesNotContain(graph.RequestedIds, id => id is 900 or 901);
    }

    [Fact]
    public async Task ScanAsync_DoesNotExpandThroughOrListANonAnimeNode()
    {
        // Reached by a traversable edge, so it is fetched — but a manga must neither be suggested nor
        // used as a bridge to whatever it links to.
        var manga = Anime(500, "Source manga", score: 85, edges: [("SEQUEL", 501)]);
        manga.Type = "MANGA";

        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SIDE_STORY", 500)]),
            manga,
            Anime(501, "Manga sequel", score: 86));

        var scan = await Scan(graph, Owned(1, CatalogStatus.Completed));

        Assert.Empty(scan.Groups);
        Assert.DoesNotContain(graph.RequestedIds, id => id == 501);
    }

    [Fact]
    public async Task ScanAsync_ExcludesThemeSongs()
    {
        var music = Anime(300, "Opening theme", score: 70);
        music.Format = "MUSIC";

        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SIDE_STORY", 300)]),
            music);

        Assert.Empty((await Scan(graph, Owned(1, CatalogStatus.Completed))).Groups);
    }

    [Fact]
    public async Task ScanAsync_IncludesSpecialsRecapsAlternativesAndUnairedSequels()
    {
        var unaired = Anime(5, "Season 3", score: null, edges: [("PREQUEL", 1)]);
        unaired.Status = "NOT_YET_RELEASED";

        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges:
            [
                ("SPIN_OFF", 2), ("SUMMARY", 3), ("ALTERNATIVE", 4), ("SEQUEL", 5)
            ]),
            Anime(2, "Spin-off", score: 75),
            Anime(3, "Recap movie", score: 60),
            Anime(4, "Reboot", score: 78),
            unaired);

        var group = Assert.Single((await Scan(graph, Owned(1, CatalogStatus.Completed))).Groups);

        Assert.Equal([4, 2, 3, 5], group.Missing.Select(item => item.AniListId));
        // Unrated sorts last rather than as a zero, and is flagged so the UI can mark it.
        Assert.False(group.Missing[^1].IsReleased);
    }

    [Fact]
    public async Task ScanAsync_TerminatesOnACycle()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "A", score: 80, edges: [("SEQUEL", 2)]),
            Anime(2, "B", score: 81, edges: [("SEQUEL", 1), ("SEQUEL", 3)]),
            Anime(3, "C", score: 82, edges: [("SEQUEL", 1)]));

        var scan = await Scan(graph, Owned(1, CatalogStatus.Completed));

        Assert.Equal(2, Assert.Single(scan.Groups).Missing.Count);
        Assert.Equal(3, scan.ScannedCount);
    }

    [Fact]
    public async Task ScanAsync_GroupsSortByTheBestThingYouAreMissing()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "Low franchise", score: 60, popularity: 10, edges: [("SEQUEL", 2)]),
            Anime(2, "Low sequel", score: 65),
            Anime(10, "High franchise", score: 70, popularity: 20, edges: [("SEQUEL", 11)]),
            Anime(11, "High sequel", score: 90));

        var scan = await Scan(graph, [
            .. Owned(1, CatalogStatus.Completed),
            .. Owned(10, CatalogStatus.Completed)
        ]);

        Assert.Equal(["High franchise", "Low franchise"], scan.Groups.Select(group => group.Title));
    }

    [Fact]
    public async Task ScanAsync_NamesAGroupAfterTheMostPopularThingYouWatchedWhenThereIsNoFranchise()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "The obscure OVA", score: 70, popularity: 500, edges: [("SEQUEL", 2)]),
            Anime(2, "Missing sequel", score: 80, edges: [("PREQUEL", 1), ("SIDE_STORY", 3)]),
            Anime(3, "The famous one", score: 88, popularity: 400_000, edges: [("SIDE_STORY", 2)]));

        var scan = await Scan(graph, [
            .. Owned(1, CatalogStatus.Completed),
            .. Owned(3, CatalogStatus.Completed)
        ]);

        Assert.Equal("The famous one", Assert.Single(scan.Groups).Title);
    }

    [Fact]
    public async Task ScanAsync_UsesTheLocalFranchiseNameWhenTheWatchedEntriesShareOne()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "Kuro no Keiyakusha", score: 77, edges: [("SEQUEL", 2)]),
            Anime(2, "Gemini", score: 71, edges: [("PREQUEL", 1)]));

        var entry = new AnimeEntry { Id = 1, AniListId = 1, TitleRomaji = "Kuro no Keiyakusha", FranchiseId = 15 };
        var snapshot = new RepositorySnapshot(
            [entry],
            [new CatalogEntry { AnimeEntryId = 1, Status = CatalogStatus.Completed }],
            [],
            [new Franchise { Id = 15, Title = "Darker than Black", Slug = "darker-than-black" }]);

        var scan = await new FranchiseGapService(graph).ScanAsync(snapshot);

        var group = Assert.Single(scan.Groups);
        Assert.Equal("Darker than Black", group.Title);
        Assert.Equal("darker-than-black", group.FranchiseSlug);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressAsBatchesLand()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "A", score: 80, edges: [("SEQUEL", 2)]),
            Anime(2, "B", score: 81, edges: [("SEQUEL", 3)]),
            Anime(3, "C", score: 82));

        var reports = new List<int>();
        var progress = new Progress<FranchiseGapScanViewModel>(partial => reports.Add(partial.ScannedCount));

        await new FranchiseGapService(graph).ScanAsync(SnapshotFor(Owned(1, CatalogStatus.Completed)), progress);

        // Progress is delivered on the synchronisation context, so allow it to drain.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task ScanAsync_StopsWhenCancelled()
    {
        var graph = new FakeEnrichmentService(
            Anime(1, "A", score: 80, edges: [("SEQUEL", 2)]),
            Anime(2, "B", score: 81));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new FranchiseGapService(graph).ScanAsync(SnapshotFor(Owned(1, CatalogStatus.Completed)), cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ScanAsync_WithNothingWatchedReturnsNothing()
    {
        var graph = new FakeEnrichmentService(Anime(1, "A", score: 80));

        var scan = await new FranchiseGapService(graph).ScanAsync(new RepositorySnapshot([], [], [], []));

        Assert.Empty(scan.Groups);
        Assert.Equal(0, scan.ScannedCount);
        Assert.False(scan.WasTruncated);
    }

    [Fact]
    public async Task ScanAsync_FallsBackToTheMeanScoreWhenThereIsNoWeightedScore()
    {
        var noWeighted = Anime(2, "Only a mean score", score: null);
        noWeighted.MeanScore = 88;

        var graph = new FakeEnrichmentService(
            Anime(1, "Season 1", score: 80, edges: [("SEQUEL", 2)]),
            noWeighted);

        var item = Assert.Single(Assert.Single((await Scan(graph, Owned(1, CatalogStatus.Completed))).Groups).Missing);

        Assert.Equal(88, item.Score);
    }

    [Fact]
    public async Task ScanAsync_RanksAGroupByItsBestEntryNotByAnAverage()
    {
        // The Attack on Titan shape: one outstanding missing entry alongside weak spin-offs. Averaging
        // would bury it beneath a franchise whose entries are all mediocre.
        var graph = new FakeEnrichmentService(
            Anime(1, "Attack on Titan", score: 85, popularity: 900, edges:
            [
                ("SIDE_STORY", 2), ("SPIN_OFF", 3), ("SIDE_STORY", 4)
            ]),
            Anime(2, "The actual finale", score: 87),
            Anime(3, "Junior High", score: 70),
            Anime(4, "Chibi shorts", score: 63),
            Anime(10, "Consistently fine show", score: 78, popularity: 800, edges: [("SEQUEL", 11)]),
            Anime(11, "Consistently fine sequel", score: 78));

        var scan = await Scan(graph, [
            .. Owned(1, CatalogStatus.Completed),
            .. Owned(10, CatalogStatus.Completed)
        ]);

        // Mean of 87/70/63 is 73, below the other group's 78 — so an average would invert this.
        Assert.Equal(["Attack on Titan", "Consistently fine show"], scan.Groups.Select(group => group.Title));
        Assert.Equal(87, scan.Groups[0].BestScore);
        Assert.Equal("The actual finale", scan.Groups[0].Missing[0].Title);
    }

    // ---- Fixtures ---------------------------------------------------------

    private static Task<FranchiseGapScanViewModel> Scan(FakeEnrichmentService graph, IReadOnlyList<OwnedEntry> owned) =>
        new FranchiseGapService(graph).ScanAsync(SnapshotFor(owned));

    private static RepositorySnapshot SnapshotFor(IReadOnlyList<OwnedEntry> owned)
    {
        var entries = owned
            .Select((item, index) => new AnimeEntry { Id = index + 1, AniListId = item.AniListId, TitleRomaji = $"Entry {item.AniListId}" })
            .ToList();

        var catalog = entries
            .Select((entry, index) => new CatalogEntry { AnimeEntryId = entry.Id, Status = owned[index].Status })
            .ToList();

        return new RepositorySnapshot(entries, catalog, [], []);
    }

    private static OwnedEntry[] Owned(int aniListId, CatalogStatus status) => [new(aniListId, status)];

    private static AniListMedia Anime(
        int id,
        string title,
        int? score = null,
        int? popularity = null,
        params (string RelationType, int TargetId)[] edges) => new()
        {
            Id = id,
            Type = "ANIME",
            Format = "TV",
            Status = "FINISHED",
            AverageScore = score,
            Popularity = popularity,
            Title = new AniListTitle { English = title },
            Relations = new AniListRelationConnection
            {
                Edges = edges
                    .Select(edge => new AniListRelationEdge
                    {
                        RelationType = edge.RelationType,
                        Node = new AniListMedia { Id = edge.TargetId }
                    })
                    .ToList()
            }
        };

    private sealed record OwnedEntry(int AniListId, CatalogStatus Status);

    /// <summary>An in-memory AniList: returns only the ids it knows, and records what was asked for.</summary>
    private sealed class FakeEnrichmentService : IAniListEnrichmentService
    {
        private readonly Dictionary<int, AniListMedia> _media;

        public FakeEnrichmentService(params AniListMedia[] media) =>
            _media = media.ToDictionary(item => item.Id);

        public List<int> RequestedIds { get; } = [];

        public Task<AniListMedia?> GetAsync(int aniListId, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(aniListId);
            return Task.FromResult(_media.GetValueOrDefault(aniListId));
        }

        public Task<IReadOnlyDictionary<int, AniListMedia>> GetManyAsync(
            IReadOnlyCollection<int> aniListIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedIds.AddRange(aniListIds);

            IReadOnlyDictionary<int, AniListMedia> result = aniListIds
                .Where(_media.ContainsKey)
                .ToDictionary(id => id, id => _media[id]);

            return Task.FromResult(result);
        }
    }
}
