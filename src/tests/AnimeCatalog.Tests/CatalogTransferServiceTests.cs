using System.Text.Json;
using AnimeCatalog.Models;
using AnimeCatalog.Models.Transfer;
using AnimeCatalog.Services;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class CatalogTransferServiceTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildExport_AttachesFranchiseSlugAndLeavesStandaloneEntriesNull()
    {
        var snapshot = new RepositorySnapshot(
            [
                Anime(1, aniListId: 356, franchiseId: 7, title: "Fate/Zero"),
                Anime(2, aniListId: 1, franchiseId: null, title: "Cowboy Bebop")
            ],
            [Catalog(1, CatalogStatus.Completed), Catalog(2, CatalogStatus.Watching)],
            [],
            [new Franchise { Id = 7, Slug = "fate", Title = "Fate" }]);

        var export = CatalogTransferService.BuildExport(snapshot, ExportedAt);

        Assert.Equal(CatalogExportFile.CurrentVersion, export.Version);
        Assert.Equal(ExportedAt, export.ExportedAt);

        // Ordered by AniList ID, so Cowboy Bebop (1) precedes Fate/Zero (356).
        Assert.Equal([1, 356], export.Entries.Select(entry => entry.AniListId));
        Assert.Null(export.Entries[0].FranchiseSlug);
        Assert.Equal("fate", export.Entries[1].FranchiseSlug);

        Assert.Single(export.Franchises);
        Assert.Equal("fate", export.Franchises[0].Slug);
    }

    [Fact]
    public void BuildExport_CarriesNoDatabaseIdentifiers()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(99, aniListId: 356, franchiseId: 7, title: "Fate/Zero")],
            [Catalog(99, CatalogStatus.Completed)],
            [],
            [new Franchise { Id = 7, Slug = "fate", Title = "Fate" }]);

        var json = JsonSerializer.Serialize(
            CatalogTransferService.BuildExport(snapshot, ExportedAt),
            CatalogTransferService.JsonOptions);

        // The portable format must not leak row IDs, or a file cannot be merged into a rebuilt database.
        Assert.DoesNotContain("\"id\"", json);
        Assert.DoesNotContain("franchiseId", json);
        Assert.DoesNotContain("animeEntryId", json);
        Assert.Contains("\"anilistId\": 356", json);
        Assert.Contains("\"franchiseSlug\": \"fate\"", json);
    }

    [Fact]
    public void ExportFormat_IsPinnedByAttributesNotByANamingPolicy()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(1, aniListId: 356, franchiseId: 7, title: "Fate/Zero")],
            [Catalog(1, CatalogStatus.Completed)],
            [new AnimeRelation { SourceAnimeId = 1, TargetAniListId = 999, RelationType = "SEQUEL" }],
            [new Franchise { Id = 7, Slug = "fate", Title = "Fate" }]);

        var export = CatalogTransferService.BuildExport(snapshot, ExportedAt);

        // Serializing under a hostile naming policy must produce byte-identical output, proving the
        // on-disk format comes from the JsonPropertyName attributes alone and cannot shift if the
        // options are ever changed.
        var hostile = new JsonSerializerOptions(CatalogTransferService.JsonOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper
        };

        Assert.Equal(
            JsonSerializer.Serialize(export, CatalogTransferService.JsonOptions),
            JsonSerializer.Serialize(export, hostile));
    }

    [Fact]
    public void BuildExport_GroupsRelationsOntoTheOwningEntry()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(1, aniListId: 356, title: "Fate/Zero"), Anime(2, aniListId: 1, title: "Cowboy Bebop")],
            [Catalog(1, CatalogStatus.Completed), Catalog(2, CatalogStatus.Completed)],
            [
                new AnimeRelation { Id = 1, SourceAnimeId = 1, TargetAniListId = 999, RelationType = "SEQUEL" },
                new AnimeRelation { Id = 2, SourceAnimeId = 1, TargetAniListId = 111, RelationType = "PREQUEL" }
            ],
            []);

        var export = CatalogTransferService.BuildExport(snapshot, ExportedAt);

        var bebop = export.Entries.Single(entry => entry.AniListId == 1);
        var fate = export.Entries.Single(entry => entry.AniListId == 356);

        Assert.Empty(bebop.Relations);
        Assert.Equal([111, 999], fate.Relations.Select(relation => relation.TargetAniListId));
        Assert.Equal("PREQUEL", fate.Relations[0].RelationType);
    }

    [Fact]
    public void BuildExport_WritesStatusAsItsApiValue()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(1, aniListId: 356, title: "Fate/Zero")],
            [Catalog(1, CatalogStatus.OnHold)],
            [],
            []);

        var export = CatalogTransferService.BuildExport(snapshot, ExportedAt);

        Assert.Equal("on_hold", export.Entries[0].Status);
    }

    [Fact]
    public void BuildExport_SkipsAnimeWithoutACatalogEntryInsteadOfThrowing()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(1, aniListId: 356, title: "Fate/Zero"), Anime(2, aniListId: 1, title: "Orphan")],
            [Catalog(1, CatalogStatus.Completed)],
            [],
            []);

        var export = CatalogTransferService.BuildExport(snapshot, ExportedAt);

        Assert.Single(export.Entries);
        Assert.Equal(356, export.Entries[0].AniListId);
    }

    [Fact]
    public void ExportFile_SurvivesAJsonRoundTrip()
    {
        var snapshot = new RepositorySnapshot(
            [Anime(1, aniListId: 356, franchiseId: 7, title: "Fate/Zero")],
            [
                new CatalogEntry
                {
                    AnimeEntryId = 1,
                    Status = CatalogStatus.Completed,
                    Score = 9.5m,
                    EpisodesWatched = 25,
                    Notes = "great",
                    StartedAt = new DateOnly(2024, 1, 2),
                    CompletedAt = new DateOnly(2024, 3, 4)
                }
            ],
            [new AnimeRelation { SourceAnimeId = 1, TargetAniListId = 999, RelationType = "SEQUEL" }],
            [new Franchise { Id = 7, Slug = "fate", Title = "Fate", Description = "Grail war" }]);

        var original = CatalogTransferService.BuildExport(snapshot, ExportedAt);
        var json = JsonSerializer.Serialize(original, CatalogTransferService.JsonOptions);
        var restored = JsonSerializer.Deserialize<CatalogExportFile>(json, CatalogTransferService.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(original.ExportedAt, restored.ExportedAt);
        Assert.Equal("fate", restored.Franchises[0].Slug);
        Assert.Equal("Grail war", restored.Franchises[0].Description);

        var entry = restored.Entries[0];
        Assert.Equal(356, entry.AniListId);
        Assert.Equal("fate", entry.FranchiseSlug);
        Assert.Equal("completed", entry.Status);
        Assert.Equal(9.5m, entry.Score);
        Assert.Equal(25, entry.EpisodesWatched);
        Assert.Equal(new DateOnly(2024, 1, 2), entry.StartedAt);
        Assert.Equal(new DateOnly(2024, 3, 4), entry.CompletedAt);
        Assert.Equal(999, entry.Relations[0].TargetAniListId);
    }

    [Fact]
    public async Task ImportAsync_CountsExistingEntriesAsUpdatedAndNewOnesAsCreated()
    {
        var supabase = new FakeSupabase();
        var existing = new RepositorySnapshot(
            [Anime(1, aniListId: 356, title: "Fate/Zero")],
            [Catalog(1, CatalogStatus.Planned)],
            [],
            []);
        var service = new CatalogTransferService(supabase, new FakeCatalog(existing), new FakeAdmin());

        var result = await service.ImportAsync(new CatalogExportFile
        {
            Entries =
            [
                ExportEntry(356, "completed"),
                ExportEntry(1, "watching")
            ]
        });

        Assert.Empty(result.Skipped);
        Assert.Equal(1, result.EntriesUpdated);
        Assert.Equal(1, result.EntriesCreated);
        Assert.Equal(2, supabase.Upserts.Count(call => call.Table == "anime_entries"));
        Assert.Equal(2, supabase.Upserts.Count(call => call.Table == "catalog_entries"));

        // Merge-only: nothing may be deleted from the entry tables.
        Assert.DoesNotContain(supabase.Deletes, table => table is "anime_entries" or "catalog_entries" or "franchises");
    }

    [Fact]
    public async Task ImportAsync_SkipsBadStatusAndStillImportsTheRest()
    {
        var supabase = new FakeSupabase();
        var service = new CatalogTransferService(
            supabase,
            new FakeCatalog(new RepositorySnapshot([], [], [], [])),
            new FakeAdmin());

        var result = await service.ImportAsync(new CatalogExportFile
        {
            Entries =
            [
                ExportEntry(356, "garbage"),
                ExportEntry(1, "completed")
            ]
        });

        var skipped = Assert.Single(result.Skipped);
        Assert.Contains("AniList 356", skipped);
        Assert.Contains("garbage", skipped);

        Assert.Equal(1, result.EntriesCreated);
        // The rejected entry must not have written anything at all.
        Assert.Single(supabase.Upserts.Where(call => call.Table == "anime_entries"));
    }

    [Fact]
    public async Task ImportAsync_LeavesRelationsAloneWhenTheFileHasNone()
    {
        var supabase = new FakeSupabase();
        var service = new CatalogTransferService(
            supabase,
            new FakeCatalog(new RepositorySnapshot([], [], [], [])),
            new FakeAdmin());

        var result = await service.ImportAsync(new CatalogExportFile
        {
            Entries = [ExportEntry(356, "completed")]
        });

        Assert.Equal(0, result.RelationsWritten);
        Assert.DoesNotContain("anime_relations", supabase.Deletes);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnUnknownFileVersion()
    {
        var service = new CatalogTransferService(
            new FakeSupabase(),
            new FakeCatalog(new RepositorySnapshot([], [], [], [])),
            new FakeAdmin());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ImportAsync(new CatalogExportFile { Version = 99 }));
    }

    [Fact]
    public async Task ImportAsync_RequiresAdmin()
    {
        var service = new CatalogTransferService(
            new FakeSupabase(),
            new FakeCatalog(new RepositorySnapshot([], [], [], [])),
            new FakeAdmin(isAdmin: false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.ImportAsync(new CatalogExportFile()));
    }

    private static AnimeEntry Anime(long id, int aniListId, string title, long? franchiseId = null) => new()
    {
        Id = id,
        AniListId = aniListId,
        FranchiseId = franchiseId,
        TitleRomaji = title
    };

    private static CatalogEntry Catalog(long animeEntryId, CatalogStatus status) => new()
    {
        AnimeEntryId = animeEntryId,
        Status = status
    };

    private static CatalogExportEntry ExportEntry(int aniListId, string status) => new()
    {
        AniListId = aniListId,
        TitleRomaji = $"Anime {aniListId}",
        Status = status
    };

    private sealed record UpsertCall(string Table, string OnConflictColumn);

    /// <summary>
    /// Only the four members import touches are implemented; the rest throw so an unexpected call
    /// fails loudly rather than silently returning nothing.
    /// </summary>
    private sealed class FakeSupabase : ISupabaseRestService
    {
        private long _nextId = 1;

        public List<UpsertCall> Upserts { get; } = [];
        public List<string> Deletes { get; } = [];
        public List<string> InsertManyTables { get; } = [];

        public bool IsConfigured => true;

        public Task<T?> UpsertSingleAsync<T>(string table, object payload, string onConflictColumn, CancellationToken cancellationToken = default)
        {
            Upserts.Add(new UpsertCall(table, onConflictColumn));

            object row = table switch
            {
                "franchises" => new AnimeCatalog.Models.Supabase.FranchiseRow { Id = _nextId++ },
                "anime_entries" => new AnimeCatalog.Models.Supabase.AnimeEntryRow { Id = _nextId++ },
                "catalog_entries" => new AnimeCatalog.Models.Supabase.CatalogEntryRow { Id = _nextId++ },
                _ => throw new InvalidOperationException($"Unexpected upsert table '{table}'.")
            };

            return Task.FromResult((T?)row);
        }

        public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
        {
            Deletes.Add(table);
            return Task.CompletedTask;
        }

        public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
        {
            InsertManyTables.Add(table);
            return Task.FromResult(new List<T>());
        }

        public Task<List<T>> SelectAsync<T>(string table, IReadOnlyDictionary<string, string>? query = null, string select = "*", CancellationToken cancellationToken = default, string? order = "id.asc")
            => Task.FromResult(new List<T>());

        public Task<T?> SelectSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, string select = "*", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> UpdateSingleAsync<T>(string table, IReadOnlyDictionary<string, string> query, object payload, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeCatalog(RepositorySnapshot snapshot) : ICatalogService
    {
        public bool IsConfigured => true;

        public Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot);

        public Task<CatalogOverlay> GetCatalogOverlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CatalogOverlay.Empty());

        public void InvalidateCatalogOverlay()
        {
        }

        public Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeAdmin(bool isAdmin = true) : IAdminAuthorizationService
    {
        public Task<bool> EnsureAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(isAdmin);
    }
}
