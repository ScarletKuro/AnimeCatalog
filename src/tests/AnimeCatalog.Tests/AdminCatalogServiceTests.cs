using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class AdminCatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 31);

    [Fact]
    public async Task SaveAsync_NewAnime_CreatesCatalogEntry()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.NextInsertIds["anime_entries"] = 101;
        supabase.CatalogEntryExistsByAnimeEntryId[101] = true;
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var id = await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            TitleEnglish = "KILL BLUE",
            TitleNative = "キルアオ",
            Status = CatalogStatus.Planned,
            EpisodesWatched = 0
        });

        Assert.Equal(101, id);
        Assert.Contains(supabase.InsertCalls, call => call.Table == "anime_entries");
        Assert.Contains(supabase.UpsertCalls, call => call.Table == "catalog_entries");
    }

    [Fact]
    public async Task SaveAsync_ExistingOrphanAnime_RecreatesMissingCatalogEntry()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.CatalogEntryExistsByAnimeEntryId[3] = true;
        var orphanAnime = new AnimeEntry
        {
            Id = 3,
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            TitleEnglish = "KILL BLUE"
        };

        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([orphanAnime], [], [], []),
            aniListMedia: CreateMedia(198113));

        var id = await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            TitleEnglish = "KILL BLUE",
            TitleNative = "キルアオ",
            Status = CatalogStatus.Planned,
            EpisodesWatched = 0
        });

        Assert.Equal(3, id);
        Assert.DoesNotContain(supabase.InsertCalls, call => call.Table == "anime_entries");
        Assert.Contains(supabase.UpsertCalls, call => call.Table == "catalog_entries");
    }

    [Fact]
    public async Task CreateDraftFromAniListAsync_RelatedFranchiseIsSuggested()
    {
        const long franchiseId = 7;
        var relatedAnime = new AnimeEntry
        {
            Id = 11,
            AniListId = 1575,
            FranchiseId = franchiseId,
            TitleRomaji = "Code Geass"
        };

        var franchise = new Franchise
        {
            Id = franchiseId,
            Title = "Code Geass",
            Slug = "code-geass"
        };

        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([relatedAnime], [], [], [franchise]),
            aniListMedia: CreateMedia(198113, "Code Geass: Lelouch of the Rebellion", relationAniListIds: [1575]));

        var draft = await service.CreateDraftFromAniListAsync(198113);

        Assert.Equal(FranchiseAssignmentMode.Existing, draft.FranchiseAssignmentMode);
        Assert.Equal(franchiseId, draft.FranchiseId);
        Assert.Equal("Code Geass", draft.SuggestedFranchiseTitle);
        Assert.Equal("Code Geass", draft.SuggestedNewFranchiseTitle);
    }

    [Fact]
    public async Task CreateDraftFromAniListAsync_DerivesSuggestedNewFranchiseTitleWithoutRelationMatch()
    {
        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113, "Code Geass: Lelouch of the Rebellion"));

        var draft = await service.CreateDraftFromAniListAsync(198113);

        Assert.Equal(FranchiseAssignmentMode.None, draft.FranchiseAssignmentMode);
        Assert.Null(draft.SuggestedFranchiseTitle);
        Assert.Equal("Code Geass", draft.SuggestedNewFranchiseTitle);
    }

    [Fact]
    public async Task CreateDraftFromAniListAsync_DoesNotSuggestFranchiseFromOtherRelation()
    {
        const long soulEaterFranchiseId = 42;
        var soulEater = new AnimeEntry
        {
            Id = 3588,
            AniListId = 3588,
            FranchiseId = soulEaterFranchiseId,
            TitleRomaji = "Soul Eater"
        };

        var soulEaterFranchise = new Franchise
        {
            Id = soulEaterFranchiseId,
            Title = "Soul Eater",
            Slug = "soul-eater"
        };

        var media = CreateMedia(179062, "Fire Force Season 3 Part 2");
        media.Relations = new AniListRelationConnection
        {
            Edges =
            [
                RelationEdge("PREQUEL", 149118, "ANIME", "TV", "Fire Force Season 3"),
                RelationEdge("SOURCE", 86310, "MANGA", "MANGA", "Fire Force"),
                RelationEdge("OTHER", 3588, "ANIME", "TV", "Soul Eater")
            ]
        };

        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([soulEater], [], [], [soulEaterFranchise]),
            aniListMedia: media);

        var draft = await service.CreateDraftFromAniListAsync(179062);

        Assert.Equal(FranchiseAssignmentMode.None, draft.FranchiseAssignmentMode);
        Assert.Null(draft.FranchiseId);
        Assert.Null(draft.SuggestedFranchiseTitle);
        Assert.Equal("Fire Force Season 3 Part 2", draft.SuggestedNewFranchiseTitle);
    }

    [Fact]
    public async Task CreateDraftFromAniListAsync_DefaultsToCompletedWithEpisodesWatchedPrefilled()
    {
        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113, episodes: 24));

        var draft = await service.CreateDraftFromAniListAsync(198113);

        Assert.Equal(CatalogStatus.Completed, draft.Status);
        Assert.Equal(24, draft.EpisodesWatched);

        // No status handler fires for a draft's default, so the date is seeded here or the entry is
        // saved as Completed with nothing for the home page to sort it by.
        Assert.Equal(Today, draft.CompletedAt);
    }

    [Fact]
    public async Task CreateDraftFromAniListAsync_AiringShowWithoutEpisodeCountWatchesZero()
    {
        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var draft = await service.CreateDraftFromAniListAsync(198113);

        Assert.Equal(CatalogStatus.Completed, draft.Status);
        Assert.Null(draft.Episodes);
        Assert.Equal(0, draft.EpisodesWatched);
    }

    [Fact]
    public async Task UpdateCatalogEntryAsync_UpsertsOnlyTheCatalogRow()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.UpdateCatalogEntryAsync(101, CatalogStatus.Watching, 8.5m, 6, null, null);

        var upsert = Assert.Single(supabase.UpsertCalls);
        Assert.Equal("catalog_entries", upsert.Table);
        Assert.Equal("anime_entry_id", upsert.OnConflictColumn);

        // Only the columns the inline editor owns are sent. notes stays deliberately absent so a
        // PostgREST merge-duplicates upsert leaves it untouched; the dates are sent because the
        // status pill decides them.
        var payload = upsert.Payload.GetType().GetProperties().Select(property => property.Name).ToList();
        Assert.Equal(
            ["anime_entry_id", "status", "score", "episodes_watched", "started_at", "completed_at"],
            payload);

        // A status click must not touch anime_entries or re-sync anime_relations.
        Assert.Empty(supabase.InsertCalls);
    }

    // The inline pills are the fast path the original bug report used, so the rule has to reach them
    // and not just the full editor.
    [Fact]
    public async Task UpdateCatalogEntryAsync_StampsTheCompletionDate()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.UpdateCatalogEntryAsync(101, CatalogStatus.Completed, 8.5m, 16, null, null);

        Assert.Equal(Today, ReadDate(Assert.Single(supabase.UpsertCalls).Payload, "completed_at"));
    }

    [Fact]
    public async Task UpdateCatalogEntryAsync_KeepsACompletionDateTheRowAlreadyHas()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var stored = new DateOnly(2024, 3, 4);
        await service.UpdateCatalogEntryAsync(101, CatalogStatus.Completed, 8.5m, 16, null, stored);

        Assert.Equal(stored, ReadDate(Assert.Single(supabase.UpsertCalls).Payload, "completed_at"));
    }

    [Fact]
    public async Task UpdateCatalogEntryAsync_ClearsTheCompletionDateWhenTheStatusLeavesCompleted()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.UpdateCatalogEntryAsync(101, CatalogStatus.Watching, null, 15, new DateOnly(2024, 3, 4), Today);

        var payload = Assert.Single(supabase.UpsertCalls).Payload;
        Assert.Null(ReadDate(payload, "completed_at"));
        Assert.Equal(new DateOnly(2024, 3, 4), ReadDate(payload, "started_at"));
    }

    // The write boundary applies the rule too, so a draft or a page that never fired a status
    // handler cannot store a date its status contradicts.
    [Fact]
    public async Task SaveAsync_ClearsADateTheStatusForbids()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.NextInsertIds["anime_entries"] = 101;
        supabase.CatalogEntryExistsByAnimeEntryId[101] = true;
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            Status = CatalogStatus.Watching,
            EpisodesWatched = 5,
            CompletedAt = new DateOnly(2024, 3, 4)
        });

        var upsert = Assert.Single(supabase.UpsertCalls, call => call.Table == "catalog_entries");
        Assert.Null(ReadDate(upsert.Payload, "completed_at"));
    }

    [Fact]
    public async Task SaveAsync_StampsACompletedEntryThatCarriesNoDate()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.NextInsertIds["anime_entries"] = 101;
        supabase.CatalogEntryExistsByAnimeEntryId[101] = true;
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            Status = CatalogStatus.Completed,
            EpisodesWatched = 16
        });

        var upsert = Assert.Single(supabase.UpsertCalls, call => call.Table == "catalog_entries");
        Assert.Equal(Today, ReadDate(upsert.Payload, "completed_at"));
    }

    // A hand-typed date is an answer, not a gap to fill.
    [Fact]
    public async Task SaveAsync_KeepsAHandTypedCompletionDate()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.NextInsertIds["anime_entries"] = 101;
        supabase.CatalogEntryExistsByAnimeEntryId[101] = true;
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var typed = new DateOnly(2019, 5, 5);
        await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            Status = CatalogStatus.Completed,
            EpisodesWatched = 16,
            CompletedAt = typed
        });

        var upsert = Assert.Single(supabase.UpsertCalls, call => call.Table == "catalog_entries");
        Assert.Equal(typed, ReadDate(upsert.Payload, "completed_at"));
    }

    private static DateOnly? ReadDate(object payload, string propertyName) =>
        (DateOnly?)payload.GetType().GetProperty(propertyName)!.GetValue(payload);

    [Fact]
    public async Task UpdateCatalogEntryAsync_RejectsAnOutOfRangeScore()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.UpdateCatalogEntryAsync(101, CatalogStatus.Watching, 11m, 6, null, null));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.UpdateCatalogEntryAsync(101, CatalogStatus.Watching, null, -1, null, null));

        Assert.Empty(supabase.UpsertCalls);
    }

    [Fact]
    public async Task InspectAniListIdAsync_ReportsAnExistingEntryInsteadOfThrowing()
    {
        // Selecting an already-added anime used to be a dead end, which hid the fact that its sequel
        // was missing. Inspection reports it instead, so the page can offer the relations.
        var existing = new AnimeEntry { Id = 480, AniListId = 143338, FranchiseId = 142, TitleRomaji = "Otonari no Tenshi-sama" };
        var franchise = new Franchise { Id = 142, Title = "The Angel Next Door", Slug = "angel" };

        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([existing], [], [], [franchise]),
            aniListMedia: CreateMedia(143338));

        var inspection = await service.InspectAniListIdAsync(143338);

        Assert.True(inspection.IsAlreadyInCatalog);
        Assert.Equal(480, inspection.ExistingEntry!.Id);
        Assert.Equal(142, inspection.ExistingFranchise!.Id);
        // No draft for something already catalogued.
        Assert.Null(inspection.Draft);
    }

    [Fact]
    public async Task InspectAniListIdAsync_BuildsADraftWhenTheAnimeIsNew()
    {
        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113, episodes: 12));

        var inspection = await service.InspectAniListIdAsync(198113);

        Assert.False(inspection.IsAlreadyInCatalog);
        Assert.Null(inspection.ExistingEntry);
        Assert.NotNull(inspection.Draft);
        Assert.Equal(198113, inspection.Draft!.AniListId);
    }

    [Fact]
    public async Task InspectAniListIdAsync_MarksWhichRelationsAreAlreadyInTheCatalog()
    {
        var sequelInCatalog = new AnimeEntry { Id = 470, AniListId = 555, TitleRomaji = "Season 2" };

        var media = CreateMedia(143338);
        media.Relations = new AniListRelationConnection
        {
            Edges =
            [
                RelationEdge("SEQUEL", 555, "ANIME", "TV", "Season 2"),
                RelationEdge("SEQUEL", 666, "ANIME", "TV", "Season 3")
            ]
        };

        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([sequelInCatalog], [], [], []),
            aniListMedia: media);

        var inspection = await service.InspectAniListIdAsync(143338);

        Assert.Equal(2, inspection.Relations.Count);
        Assert.True(inspection.Relations.Single(r => r.AniListId == 555).IsInCatalog);
        Assert.Equal(666, Assert.Single(inspection.MissingRelations).AniListId);
    }

    [Theory]
    [InlineData("SEQUEL", "MANGA", "MANGA")]   // the source manga
    [InlineData("SEQUEL", "ANIME", "MUSIC")]   // a theme song
    [InlineData("CHARACTER", "ANIME", "TV")]   // a cameo
    [InlineData("OTHER", "ANIME", "TV")]
    public async Task InspectAniListIdAsync_ExcludesRelationsThisCatalogDoesNotHold(string relationType, string nodeType, string nodeFormat)
    {
        var media = CreateMedia(143338);
        media.Relations = new AniListRelationConnection
        {
            Edges = [RelationEdge(relationType, 999, nodeType, nodeFormat, "Not an addable anime")]
        };

        var service = CreateService(
            new FakeSupabaseRestService(),
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: media);

        var inspection = await service.InspectAniListIdAsync(143338);

        Assert.Empty(inspection.Relations);
    }

    private static AniListRelationEdge RelationEdge(string relationType, int id, string type, string format, string title) => new()
    {
        RelationType = relationType,
        Node = new AniListMedia
        {
            Id = id,
            Type = type,
            Format = format,
            Title = new AniListTitle { English = title }
        }
    };


    [Fact]
    public async Task GetCatalogedAniListIdsAsync_MapsAniListIdToLocalEntryId()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.AnimeEntryRows.Add(new AnimeEntryRow { Id = 7, AniListId = 198113 });
        supabase.AnimeEntryRows.Add(new AnimeEntryRow { Id = 9, AniListId = 21 });

        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var map = await service.GetCatalogedAniListIdsAsync();

        Assert.Equal(2, map.Count);
        Assert.Equal(7, map[198113]);
        Assert.Equal(9, map[21]);
    }

    [Fact]
    public async Task GetCatalogedAniListIdsAsync_ReadsOnlyTheTwoColumnsItNeeds()
    {
        var supabase = new FakeSupabaseRestService();
        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        await service.GetCatalogedAniListIdsAsync();

        Assert.Equal([("anime_entries", "id,anilist_id")], supabase.SelectCalls);
    }

    [Fact]
    public async Task GetCatalogedAniListIdsAsync_SurvivesDuplicateAniListIds()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.AnimeEntryRows.Add(new AnimeEntryRow { Id = 7, AniListId = 198113 });
        supabase.AnimeEntryRows.Add(new AnimeEntryRow { Id = 8, AniListId = 198113 });

        var service = CreateService(
            supabase,
            snapshot: new RepositorySnapshot([], [], [], []),
            aniListMedia: CreateMedia(198113));

        var map = await service.GetCatalogedAniListIdsAsync();

        Assert.Equal(7, Assert.Single(map).Value);
    }

    // CatalogService caches the four-table snapshot, so a write that does not say so leaves the
    // writer looking at the version they just replaced. One test per write path, because each one
    // reaches Supabase by a different route and only SaveAsync is shared.
    [Fact]
    public async Task UpdateCatalogEntryAsync_DropsTheCachedReads()
    {
        var catalog = new FakeCatalogService(new RepositorySnapshot([], [], [], []));
        var service = CreateService(new FakeSupabaseRestService(), catalog, CreateMedia(198113));

        await service.UpdateCatalogEntryAsync(101, CatalogStatus.Watching, 8.5m, 6, null, null);

        Assert.Equal(1, catalog.CacheInvalidations);
    }

    [Fact]
    public async Task DeleteAsync_DropsTheCachedReads()
    {
        var catalog = new FakeCatalogService(new RepositorySnapshot([], [], [], []));
        var service = CreateService(new FakeSupabaseRestService(), catalog, CreateMedia(198113));

        await service.DeleteAsync(101);

        Assert.Equal(1, catalog.CacheInvalidations);
    }

    [Fact]
    public async Task SaveAsync_DropsTheCachedReads()
    {
        var supabase = new FakeSupabaseRestService();
        supabase.NextInsertIds["anime_entries"] = 101;
        supabase.CatalogEntryExistsByAnimeEntryId[101] = true;
        var catalog = new FakeCatalogService(new RepositorySnapshot([], [], [], []));
        var service = CreateService(supabase, catalog, CreateMedia(198113));

        await service.SaveAsync(new AnimeEditorModel
        {
            AniListId = 198113,
            TitleRomaji = "Kill Ao",
            Status = CatalogStatus.Planned
        });

        Assert.Equal(1, catalog.CacheInvalidations);
    }

    // A failed write must not drop the cache: the rows in memory still describe what is stored, and
    // throwing them away would cost a re-read to arrive back at the same answer.
    [Fact]
    public async Task AFailedSaveLeavesTheCachedReadsAlone()
    {
        var catalog = new FakeCatalogService(new RepositorySnapshot([], [], [], []));
        var service = CreateService(new FakeSupabaseRestService(), catalog, CreateMedia(198113));

        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveAsync(new AnimeEditorModel
        {
            // No title, so validation refuses the model before anything is written.
            AniListId = 198113,
            Status = CatalogStatus.Watching
        }));

        Assert.Equal(0, catalog.CacheInvalidations);
    }

    private static AdminCatalogService CreateService(
        FakeSupabaseRestService supabase,
        RepositorySnapshot snapshot,
        AniListMedia aniListMedia)
    {
        return CreateService(supabase, new FakeCatalogService(snapshot), aniListMedia);
    }

    private static AdminCatalogService CreateService(
        FakeSupabaseRestService supabase,
        FakeCatalogService catalogService,
        AniListMedia aniListMedia)
    {
        return new AdminCatalogService(
            supabase,
            new FakeAniListService(aniListMedia),
            new FakeAdminAuthorizationService(),
            catalogService,
            new FixedTimeProvider(Now));
    }

    private static AniListMedia CreateMedia(int id, string englishTitle = "KILL BLUE", IEnumerable<int>? relationAniListIds = null, int? episodes = null)
    {
        return new AniListMedia
        {
            Id = id,
            Episodes = episodes,
            Title = new AniListTitle
            {
                Romaji = "Kill Ao",
                English = englishTitle,
                Native = "キルアオ"
            },
            CoverImage = new AniListCoverImage
            {
                Large = "https://example.com/cover.jpg"
            },
            Relations = new AniListRelationConnection
            {
                Edges = relationAniListIds?.Select(relatedId => new AniListRelationEdge
                {
                    RelationType = "SEQUEL",
                    Node = new AniListMedia
                    {
                        Id = relatedId,
                        Title = new AniListTitle
                        {
                            English = $"Related {relatedId}",
                            Romaji = $"Related {relatedId}"
                        }
                    }
                }).ToList() ?? []
            }
        };
    }

    private sealed class FakeSupabaseRestService : ISupabaseRestService
    {
        public Dictionary<string, long> NextInsertIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Table, object Payload)> InsertCalls { get; } = [];
        public List<(string Table, object Payload, string OnConflictColumn)> UpsertCalls { get; } = [];
        public Dictionary<long, bool> CatalogEntryExistsByAnimeEntryId { get; } = [];
        public List<AnimeEntryRow> AnimeEntryRows { get; } = [];
        public List<(string Table, string Select)> SelectCalls { get; } = [];

        public bool IsConfigured => true;

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<T>());

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
        {
            InsertCalls.Add((table, payload));

            if (!NextInsertIds.TryGetValue(table, out var id))
            {
                return Task.FromResult(default(T));
            }

            if (typeof(T) == typeof(Dictionary<string, object>))
            {
                var result = new Dictionary<string, object> { ["id"] = id };
                return Task.FromResult((T?)(object)result);
            }

            if (typeof(T) == typeof(AnimeEntryRow))
            {
                return Task.FromResult((T?)(object)new AnimeEntryRow { Id = id });
            }

            if (typeof(T) == typeof(FranchiseRow))
            {
                return Task.FromResult((T?)(object)new FranchiseRow { Id = id });
            }

            return Task.FromResult(default(T));
        }

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
        {
            UpsertCalls.Add((table, payload, onConflictColumn));

            if (table == "catalog_entries")
            {
                var animeEntryId = ReadLong(payload, "anime_entry_id");
                CatalogEntryExistsByAnimeEntryId[animeEntryId] = true;
                if (typeof(T) == typeof(Dictionary<string, object>))
                {
                    return Task.FromResult((T?)(object)new Dictionary<string, object>
                    {
                        ["anime_entry_id"] = animeEntryId
                    });
                }

                if (typeof(T) == typeof(CatalogEntryRow))
                {
                    return Task.FromResult((T?)(object)new CatalogEntryRow
                    {
                        AnimeEntryId = animeEntryId,
                        Status = "planned",
                        EpisodesWatched = 0
                    });
                }
            }

            return Task.FromResult(default(T));
        }

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
            => Task.FromResult(default(T));

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
        {
            SelectCalls.Add((table, select));

            if (table == "anime_entries" && typeof(T) == typeof(AnimeEntryRow))
            {
                return Task.FromResult(AnimeEntryRows.Cast<T>().ToList());
            }

            return Task.FromResult(new List<T>());
        }

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
        {
            if (table == "catalog_entries" &&
                query.TryGetValue("anime_entry_id", out var rawAnimeEntryId) &&
                TryParseEqLong(rawAnimeEntryId, out var animeEntryId) &&
                CatalogEntryExistsByAnimeEntryId.GetValueOrDefault(animeEntryId))
            {
                if (typeof(T) == typeof(Dictionary<string, object>))
                {
                    return Task.FromResult((T?)(object)new Dictionary<string, object>
                    {
                        ["anime_entry_id"] = animeEntryId
                    });
                }

                if (typeof(T) == typeof(CatalogEntryRow))
                {
                    return Task.FromResult((T?)(object)new CatalogEntryRow
                    {
                        AnimeEntryId = animeEntryId,
                        Status = "planned",
                        EpisodesWatched = 0
                    });
                }
            }

            return Task.FromResult(default(T));
        }

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
            => Task.FromResult(default(T));

        private static long ReadLong(object payload, string propertyName)
        {
            var property = payload.GetType().GetProperty(propertyName);
            return Convert.ToInt64(property!.GetValue(payload));
        }

        private static bool TryParseEqLong(string rawValue, out long value)
        {
            if (rawValue.StartsWith("eq.", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(rawValue[3..], out value))
            {
                return true;
            }

            value = 0;
            return false;
        }
    }

    private sealed class FakeAniListService : IAniListService
    {
        private readonly AniListMedia _media;

        public FakeAniListService(AniListMedia media)
        {
            _media = media;
        }

        public Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(_media);

        public Task<AniListPageResult<AniListAiringSchedule>> GetAiringSchedulesAsync(
            DateTimeOffset windowStartInclusive,
            DateTimeOffset windowEndExclusive,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This stub does not serve the calendar.");

        public Task<AniListPageResult<AniListMedia>> BrowseMediaAsync(
            AniListBrowseRequest request,
            int page,
            int perPage,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This stub does not serve the calendar.");

        public Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AniListMedia>>([]);

        public Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<AniListMedia?>(_media);

        public Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AniListMedia>>(ids.Contains(_media.Id) ? [_media] : []);
    }

    private sealed class FakeAdminAuthorizationService : IAdminAuthorizationService
    {
        public Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeCatalogService : ICatalogService
    {
        private readonly RepositorySnapshot _snapshot;

        public FakeCatalogService(RepositorySnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool IsConfigured => true;

        public Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> CanCurrentUserReadCatalogAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task<CatalogOverlay> GetCatalogOverlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CatalogOverlay.Empty());

        public void InvalidateCachedReads()
        {
            CacheInvalidations++;
        }

        public int CacheInvalidations { get; private set; }
    }
}
