using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.ViewModels;
using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly ISupabaseRestService _supabaseRestService;
    private readonly FranchiseService _franchiseService;
    private readonly ICatalogAccessService _catalogAccessService;

    public CatalogService(
        ISupabaseRestService supabaseRestService,
        FranchiseService franchiseService,
        ICatalogAccessService catalogAccessService)
    {
        _supabaseRestService = supabaseRestService;
        _franchiseService = franchiseService;
        _catalogAccessService = catalogAccessService;
    }

    public bool IsConfigured => _supabaseRestService.IsConfigured;

    public async Task<IReadOnlyList<FranchiseSummaryViewModel>> GetCatalogAsync(CatalogFilters? filters = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        return _franchiseService.BuildCatalog(snapshot.AnimeEntries, snapshot.CatalogEntries, snapshot.Relations, snapshot.Franchises, filters ?? new CatalogFilters());
    }

    public async Task<HomeSummaryViewModel> GetHomeSummaryAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(cancellationToken: cancellationToken);
        return _franchiseService.BuildHomeSummary(catalog, DateTimeOffset.UtcNow);
    }

    public async Task<FranchiseDetailsViewModel?> GetFranchiseAsync(string slug, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var franchise = snapshot.Franchises.SingleOrDefault(item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return franchise is null
            ? null
            : _franchiseService.BuildFranchiseDetails(franchise, snapshot.AnimeEntries, snapshot.CatalogEntries, snapshot.Relations);
    }

    public async Task<AnimeDetailsViewModel?> GetAnimeDetailsAsync(long id, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var animeEntry = snapshot.AnimeEntries.SingleOrDefault(item => item.Id == id);
        if (animeEntry is null)
        {
            return null;
        }

        var catalogEntry = snapshot.GetRequiredCatalogEntry(id);

        var franchise = animeEntry.FranchiseId is null
            ? null
            : snapshot.Franchises.SingleOrDefault(item => item.Id == animeEntry.FranchiseId.Value);

        return _franchiseService.BuildAnimeDetails(
            animeEntry,
            catalogEntry,
            snapshot.Relations,
            franchise,
            snapshot.AnimeEntries,
            snapshot.CatalogEntries);
    }

    public async Task<AdminDashboardViewModel> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var publicCatalogEnabled = await _catalogAccessService.GetPublicCatalogEnabledAsync(cancellationToken);
        return _franchiseService.BuildAdminSummary(
            snapshot.AnimeEntries,
            snapshot.CatalogEntries,
            snapshot.Relations,
            snapshot.Franchises,
            publicCatalogEnabled);
    }

    public async Task<IReadOnlyList<Franchise>> GetFranchisesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _supabaseRestService.SelectAsync<FranchiseRow>("franchises", cancellationToken: cancellationToken);
        return rows.Select(Map).OrderBy(item => item.Title).ToList();
    }

    public async Task<AnimeEditorModel?> GetEditorModelAsync(long id, CancellationToken cancellationToken = default)
    {
        var details = await GetAnimeDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return null;
        }

        return new AnimeEditorModel
        {
            AnimeEntryId = details.AnimeEntry.Id,
            CatalogEntryId = details.CatalogEntry.Id == 0 ? null : details.CatalogEntry.Id,
            AniListId = details.AnimeEntry.AniListId,
            FranchiseId = details.AnimeEntry.FranchiseId,
            TitleRomaji = details.AnimeEntry.TitleRomaji,
            TitleEnglish = details.AnimeEntry.TitleEnglish,
            TitleNative = details.AnimeEntry.TitleNative,
            CoverUrl = details.AnimeEntry.CoverUrl,
            Format = details.AnimeEntry.Format,
            Season = details.AnimeEntry.Season,
            SeasonYear = details.AnimeEntry.SeasonYear,
            Episodes = details.AnimeEntry.Episodes,
            StartDate = details.AnimeEntry.StartDate,
            EndDate = details.AnimeEntry.EndDate,
            SeasonNumber = details.AnimeEntry.SeasonNumber,
            PartNumber = details.AnimeEntry.PartNumber,
            DisplayOrder = details.AnimeEntry.DisplayOrder,
            Status = details.CatalogEntry.Status,
            Score = details.CatalogEntry.Score,
            EpisodesWatched = details.CatalogEntry.EpisodesWatched,
            Notes = details.CatalogEntry.Notes,
            StartedAt = details.CatalogEntry.StartedAt,
            CompletedAt = details.CatalogEntry.CompletedAt,
            FranchiseAssignmentMode = details.Franchise is null ? FranchiseAssignmentMode.None : FranchiseAssignmentMode.Existing,
            SuggestedFranchiseTitle = details.Franchise?.Title,
            SuggestedNewFranchiseTitle = FranchiseTitleSuggester.Build(details.AnimeEntry.TitleEnglish, details.AnimeEntry.TitleRomaji)
        };
    }

    public async Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _catalogAccessService.CanCurrentUserReadCatalogAsync(cancellationToken))
            {
                throw new CatalogAccessDeniedException();
            }

            var animeRowsTask = _supabaseRestService.SelectAsync<AnimeEntryRow>("anime_entries", cancellationToken: cancellationToken);
            var catalogRowsTask = _supabaseRestService.SelectAsync<CatalogEntryRow>("catalog_entries", cancellationToken: cancellationToken);
            var relationRowsTask = _supabaseRestService.SelectAsync<AnimeRelationRow>("anime_relations", cancellationToken: cancellationToken);
            var franchiseRowsTask = _supabaseRestService.SelectAsync<FranchiseRow>("franchises", cancellationToken: cancellationToken);

            await Task.WhenAll(animeRowsTask, catalogRowsTask, relationRowsTask, franchiseRowsTask);

            return new RepositorySnapshot(
                animeRowsTask.Result.Select(Map).ToList(),
                catalogRowsTask.Result.Select(Map).ToList(),
                relationRowsTask.Result.Select(Map).ToList(),
                franchiseRowsTask.Result.Select(Map).ToList());
        }
        catch (Exception ex) when (CatalogAccess.IsPrivateAccessDenied(ex))
        {
            throw new CatalogAccessDeniedException();
        }
    }

    private static AnimeEntry Map(AnimeEntryRow row) => new()
    {
        Id = row.Id,
        AniListId = row.AniListId,
        FranchiseId = row.FranchiseId,
        TitleRomaji = row.TitleRomaji,
        TitleEnglish = row.TitleEnglish,
        TitleNative = row.TitleNative,
        CoverUrl = row.CoverUrl,
        Format = row.Format,
        Season = row.Season,
        SeasonYear = row.SeasonYear,
        Episodes = row.Episodes,
        StartDate = row.StartDate,
        EndDate = row.EndDate,
        SeasonNumber = row.SeasonNumber,
        PartNumber = row.PartNumber,
        DisplayOrder = row.DisplayOrder,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static CatalogEntry Map(CatalogEntryRow row) => new()
    {
        Id = row.Id,
        AnimeEntryId = row.AnimeEntryId,
        Status = CatalogStatusExtensions.Parse(row.Status),
        Score = row.Score,
        EpisodesWatched = row.EpisodesWatched,
        Notes = row.Notes,
        StartedAt = row.StartedAt,
        CompletedAt = row.CompletedAt,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static AnimeRelation Map(AnimeRelationRow row) => new()
    {
        Id = row.Id,
        SourceAnimeId = row.SourceAnimeId,
        TargetAniListId = row.TargetAniListId,
        RelationType = row.RelationType
    };

    private static Franchise Map(FranchiseRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Slug = row.Slug,
        CoverUrl = row.CoverUrl,
        Description = row.Description,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };
}

public sealed record RepositorySnapshot(
    IReadOnlyList<AnimeEntry> AnimeEntries,
    IReadOnlyList<CatalogEntry> CatalogEntries,
    IReadOnlyList<AnimeRelation> Relations,
    IReadOnlyList<Franchise> Franchises)
{
    public CatalogEntry GetRequiredCatalogEntry(long animeEntryId)
    {
        var existing = CatalogEntries.SingleOrDefault(item => item.AnimeEntryId == animeEntryId);
        if (existing is not null)
        {
            return existing;
        }

        throw new InvalidOperationException($"Catalog entry for anime_entry_id={animeEntryId} is missing.");
    }
}
