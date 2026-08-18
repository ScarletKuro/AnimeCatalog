using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Models.Transfer;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

/// <summary>
/// Exports the catalog to a portable JSON file and merges such a file back in.
/// </summary>
/// <remarks>
/// Import deliberately does not reuse <see cref="AdminCatalogService.SaveAsync"/>: that path reads a
/// full four-table snapshot and makes a live AniList call per entry, which does not scale to a bulk
/// import. Here the snapshot is read once and relations come from the file instead.
/// <para>
/// Import is merge-only. Nothing is ever deleted from anime_entries, catalog_entries or franchises,
/// so a wrong file cannot destroy the catalog.
/// </para>
/// </remarks>
public sealed class CatalogTransferService
{
    /// <summary>
    /// Shared by read and write so the on-disk format cannot drift between them. No naming policy is
    /// set: every property on the transfer DTOs declares its own <c>JsonPropertyName</c>, so the
    /// attributes alone define the format. Reads stay case-insensitive for hand-edited files.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ISupabaseRestService _supabaseRestService;
    private readonly ICatalogService _catalogService;
    private readonly IAdminAuthorizationService _authService;

    public CatalogTransferService(
        ISupabaseRestService supabaseRestService,
        ICatalogService catalogService,
        IAdminAuthorizationService authService)
    {
        _supabaseRestService = supabaseRestService;
        _catalogService = catalogService;
        _authService = authService;
    }

    /// <summary>
    /// Pure projection of a snapshot onto the file format. <paramref name="exportedAt"/> is passed in
    /// rather than read from the clock so the output is deterministic and testable.
    /// </summary>
    public static CatalogExportFile BuildExport(RepositorySnapshot snapshot, DateTimeOffset exportedAt)
    {
        var franchiseSlugById = snapshot.Franchises.ToDictionary(item => item.Id, item => item.Slug);
        var catalogByAnimeId = snapshot.CatalogEntries.ToDictionary(item => item.AnimeEntryId);
        var relationsBySourceId = snapshot.Relations
            .GroupBy(item => item.SourceAnimeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var entries = new List<CatalogExportEntry>();

        foreach (var anime in snapshot.AnimeEntries.OrderBy(item => item.AniListId))
        {
            // An anime with no catalog row is skipped rather than thrown on: one orphan must not
            // take down the whole export.
            if (!catalogByAnimeId.TryGetValue(anime.Id, out var catalogEntry))
            {
                continue;
            }

            entries.Add(new CatalogExportEntry
            {
                AniListId = anime.AniListId,
                FranchiseSlug = anime.FranchiseId is not null && franchiseSlugById.TryGetValue(anime.FranchiseId.Value, out var slug)
                    ? slug
                    : null,
                TitleRomaji = anime.TitleRomaji,
                TitleEnglish = anime.TitleEnglish,
                TitleNative = anime.TitleNative,
                CoverUrl = anime.CoverUrl,
                Format = anime.Format,
                Season = anime.Season,
                SeasonYear = anime.SeasonYear,
                Episodes = anime.Episodes,
                StartDate = anime.StartDate,
                EndDate = anime.EndDate,
                SeasonNumber = anime.SeasonNumber,
                PartNumber = anime.PartNumber,
                DisplayOrder = anime.DisplayOrder,
                Status = catalogEntry.Status.ToApiValue(),
                Score = catalogEntry.Score,
                EpisodesWatched = catalogEntry.EpisodesWatched,
                Notes = catalogEntry.Notes,
                StartedAt = catalogEntry.StartedAt,
                CompletedAt = catalogEntry.CompletedAt,
                Relations = relationsBySourceId.GetValueOrDefault(anime.Id, [])
                    .OrderBy(item => item.TargetAniListId)
                    .ThenBy(item => item.RelationType, StringComparer.Ordinal)
                    .Select(item => new CatalogExportRelation
                    {
                        TargetAniListId = item.TargetAniListId,
                        RelationType = item.RelationType
                    })
                    .ToList()
            });
        }

        return new CatalogExportFile
        {
            Version = CatalogExportFile.CurrentVersion,
            ExportedAt = exportedAt,
            Franchises = snapshot.Franchises
                .OrderBy(item => item.Slug, StringComparer.Ordinal)
                .Select(item => new CatalogExportFranchise
                {
                    Slug = item.Slug,
                    Title = item.Title,
                    CoverUrl = item.CoverUrl,
                    Description = item.Description
                })
                .ToList(),
            Entries = entries
        };
    }

    public async Task<CatalogExportFile> ExportAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);
        var snapshot = await _catalogService.GetSnapshotAsync(cancellationToken);
        return BuildExport(snapshot, DateTimeOffset.UtcNow);
    }

    public async Task<CatalogImportResult> ImportAsync(CatalogExportFile file, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        if (file.Version != CatalogExportFile.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported backup version {file.Version}. This build reads version {CatalogExportFile.CurrentVersion}.");
        }

        var result = new CatalogImportResult();

        // Read once. These sets are what tell "created" apart from "updated" without re-querying.
        var snapshot = await _catalogService.GetSnapshotAsync(cancellationToken);
        var existingSlugs = snapshot.Franchises.Select(item => item.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingAniListIds = snapshot.AnimeEntries.Select(item => item.AniListId).ToHashSet();

        var franchiseIdBySlug = snapshot.Franchises.ToDictionary(
            item => item.Slug,
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var franchise in file.Franchises)
        {
            if (string.IsNullOrWhiteSpace(franchise.Slug))
            {
                result.Skipped.Add($"Franchise '{franchise.Title}': missing slug.");
                continue;
            }

            try
            {
                var row = await _supabaseRestService.UpsertSingleAsync<FranchiseRow>("franchises", new
                {
                    title = franchise.Title,
                    slug = franchise.Slug,
                    cover_url = franchise.CoverUrl,
                    description = franchise.Description
                }, "slug", cancellationToken) ?? throw new InvalidOperationException("Franchise upsert returned no data.");

                franchiseIdBySlug[franchise.Slug] = row.Id;

                if (existingSlugs.Contains(franchise.Slug))
                {
                    result.FranchisesUpdated++;
                }
                else
                {
                    result.FranchisesCreated++;
                }
            }
            catch (Exception ex)
            {
                result.Skipped.Add($"Franchise '{franchise.Slug}': {ex.Message}");
            }
        }

        foreach (var entry in file.Entries)
        {
            try
            {
                // Validated before any write so a bad status cannot leave a half-written entry.
                var status = CatalogStatusExtensions.Parse(entry.Status);

                long? franchiseId = entry.FranchiseSlug is not null
                    && franchiseIdBySlug.TryGetValue(entry.FranchiseSlug, out var resolvedFranchiseId)
                        ? resolvedFranchiseId
                        : null;

                var animeRow = await _supabaseRestService.UpsertSingleAsync<AnimeEntryRow>("anime_entries", new
                {
                    anilist_id = entry.AniListId,
                    franchise_id = franchiseId,
                    title_romaji = entry.TitleRomaji,
                    title_english = entry.TitleEnglish,
                    title_native = entry.TitleNative,
                    cover_url = entry.CoverUrl,
                    format = entry.Format,
                    season = entry.Season,
                    season_year = entry.SeasonYear,
                    episodes = entry.Episodes,
                    start_date = entry.StartDate,
                    end_date = entry.EndDate,
                    season_number = entry.SeasonNumber,
                    part_number = entry.PartNumber,
                    display_order = entry.DisplayOrder
                }, "anilist_id", cancellationToken) ?? throw new InvalidOperationException("Anime upsert returned no data.");

                await _supabaseRestService.UpsertSingleAsync<CatalogEntryRow>("catalog_entries", new
                {
                    anime_entry_id = animeRow.Id,
                    status = status.ToApiValue(),
                    score = entry.Score,
                    episodes_watched = entry.EpisodesWatched,
                    notes = entry.Notes,
                    started_at = entry.StartedAt,
                    completed_at = entry.CompletedAt
                }, "anime_entry_id", cancellationToken);

                // Only touch relations when the file actually carries some. Without this guard, a
                // file exported before relations were cached would wipe good cached relations.
                if (entry.Relations.Count > 0)
                {
                    await _supabaseRestService.DeleteAsync("anime_relations", new Dictionary<string, string>
                    {
                        ["source_anime_id"] = $"eq.{animeRow.Id}"
                    }, cancellationToken);

                    await _supabaseRestService.InsertManyAsync<AnimeRelationRow>(
                        "anime_relations",
                        entry.Relations.Select(relation => (object)new
                        {
                            source_anime_id = animeRow.Id,
                            target_anilist_id = relation.TargetAniListId,
                            relation_type = relation.RelationType
                        }),
                        cancellationToken);

                    result.RelationsWritten += entry.Relations.Count;
                }

                if (existingAniListIds.Contains(entry.AniListId))
                {
                    result.EntriesUpdated++;
                }
                else
                {
                    result.EntriesCreated++;
                }
            }
            catch (Exception ex)
            {
                result.Skipped.Add($"AniList {entry.AniListId}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task EnsureAdminOrThrowAsync(CancellationToken cancellationToken)
    {
        if (!await _authService.EnsureAdminAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Admin access is required.");
        }
    }
}
