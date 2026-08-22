using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.ViewModels;
using AnimeCatalog.Infrastructure;

namespace AnimeCatalog.Services;

public sealed class CatalogService : ICatalogService
{
    /// <summary>
    /// How long a catalog overlay is reused. Short, because the owner editing an entry should see it
    /// reflected soon, but long enough that paging weeks back and forth does not re-read the tables
    /// on every navigation.
    /// </summary>
    private static readonly TimeSpan OverlayTtl = TimeSpan.FromMinutes(5);

    /// <summary>A refusal is held briefly so a private catalog is not re-asked on every render.</summary>
    private static readonly TimeSpan OverlayFailureTtl = TimeSpan.FromMinutes(2);

    private readonly ISupabaseRestService _supabaseRestService;
    private readonly FranchiseService _franchiseService;
    private readonly ICatalogAccessService _catalogAccessService;
    private readonly TimeProvider _timeProvider;

    private CatalogOverlay? _overlay;
    private DateTimeOffset _overlayExpiresAt = DateTimeOffset.MinValue;

    // TimeProvider is a trailing optional parameter on purpose: DI fills it from the registered
    // singleton, and the eight existing test call sites that pass three arguments keep compiling.
    public CatalogService(
        ISupabaseRestService supabaseRestService,
        FranchiseService franchiseService,
        ICatalogAccessService catalogAccessService,
        TimeProvider? timeProvider = null)
    {
        _supabaseRestService = supabaseRestService;
        _franchiseService = franchiseService;
        _catalogAccessService = catalogAccessService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        return _franchiseService.BuildHomeSummary(catalog, _timeProvider.GetUtcNow());
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

    /// <summary>
    /// Maps AniList id to the local entry and its watch progress, for decorating pages whose primary
    /// data comes from AniList.
    /// </summary>
    /// <remarks>
    /// Unlike every other method on this service, this NEVER throws for a refusal. The calendar's
    /// AniList half has to render whether or not Supabase is configured, reachable, or readable by
    /// this visitor, so a refusal arrives as <see cref="CatalogOverlay.State"/> and an empty map.
    /// Cancellation still propagates - that means the caller navigated away, not that access was
    /// denied, and caching it as a refusal would poison the next visit.
    /// </remarks>
    public async Task<CatalogOverlay> GetCatalogOverlayAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return CatalogOverlay.Empty(CatalogAccessState.NotConfigured);
        }

        var now = _timeProvider.GetUtcNow();

        if (_overlay is not null && _overlayExpiresAt > now)
        {
            return _overlay;
        }

        try
        {
            var snapshot = await GetSnapshotAsync(cancellationToken);
            return CacheOverlay(Project(snapshot), now, OverlayTtl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (CatalogAccess.IsPrivateAccessDenied(ex))
        {
            return CacheOverlay(CatalogOverlay.Empty(CatalogAccessState.Private), now, OverlayFailureTtl);
        }
        catch
        {
            return CacheOverlay(CatalogOverlay.Empty(CatalogAccessState.Error), now, OverlayFailureTtl);
        }
    }

    /// <summary>Drops the cached overlay so the next read reflects a write that just happened.</summary>
    public void InvalidateCatalogOverlay()
    {
        _overlay = null;
        _overlayExpiresAt = DateTimeOffset.MinValue;
    }

    private static CatalogOverlay Project(RepositorySnapshot snapshot)
    {
        var catalogByAnimeId = snapshot.CatalogEntries
            .GroupBy(entry => entry.AnimeEntryId)
            .ToDictionary(group => group.Key, group => group.First());

        // GroupBy rather than ToDictionary: anime_entries has no uniqueness constraint on
        // anilist_id, so a duplicate would throw here and take the whole page down over a
        // decoration. Entries with no AniList counterpart (id 0) cannot be matched at all.
        var byAniListId = snapshot.AnimeEntries
            .Where(entry => entry.AniListId > 0)
            .GroupBy(entry => entry.AniListId)
            .ToDictionary(group => group.Key, group => ProjectItem(group.First(), catalogByAnimeId));

        return new CatalogOverlay(byAniListId, CatalogAccessState.Available);
    }

    private static CatalogOverlayItem ProjectItem(
        AnimeEntry entry,
        IReadOnlyDictionary<long, CatalogEntry> catalogByAnimeId)
    {
        var catalogEntry = catalogByAnimeId.GetValueOrDefault(entry.Id);

        return new CatalogOverlayItem(
            entry.Id,
            entry.AniListId,
            catalogEntry?.Status ?? CatalogStatus.Planned,
            catalogEntry?.EpisodesWatched ?? 0,
            catalogEntry?.Score,
            entry.Episodes);
    }

    private CatalogOverlay CacheOverlay(CatalogOverlay overlay, DateTimeOffset now, TimeSpan ttl)
    {
        _overlay = overlay;
        _overlayExpiresAt = now + ttl;
        return overlay;
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
