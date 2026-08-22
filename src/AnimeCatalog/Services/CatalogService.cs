using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.ViewModels;
using AnimeCatalog.Infrastructure;
using Microsoft.AspNetCore.Components;

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

    /// <summary>
    /// A backstop on the four-table snapshot, not the main bound on it - a navigation drops it long
    /// before this expires (see the remarks on <see cref="GetSnapshotAsync"/>). What is left for the
    /// TTL to cover is a tab parked on one path for a long time, filtering and sorting in place.
    /// </summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(60);

    private readonly ISupabaseRestService _supabaseRestService;
    private readonly FranchiseService _franchiseService;
    private readonly ICatalogAccessService _catalogAccessService;
    private readonly TimeProvider _timeProvider;
    private readonly IAuthStateNotifier? _authStateNotifier;
    private readonly NavigationManager? _navigationManager;

    private CatalogOverlay? _overlay;
    private DateTimeOffset _overlayExpiresAt = DateTimeOffset.MinValue;

    private RepositorySnapshot? _snapshot;
    private DateTimeOffset _snapshotExpiresAt = DateTimeOffset.MinValue;
    private string? _snapshotPath;
    private string? _snapshotIdentity;

    // The last three are trailing optional parameters on purpose: DI fills each from its registered
    // service, and the eight existing test call sites that pass three arguments keep compiling. A
    // test that supplies neither notifier nor navigation gets a cache keyed on "anonymous" at a
    // fixed path, which is a fair description of a test with no session and no address bar.
    public CatalogService(
        ISupabaseRestService supabaseRestService,
        FranchiseService franchiseService,
        ICatalogAccessService catalogAccessService,
        TimeProvider? timeProvider = null,
        IAuthStateNotifier? authStateNotifier = null,
        NavigationManager? navigationManager = null)
    {
        _supabaseRestService = supabaseRestService;
        _franchiseService = franchiseService;
        _catalogAccessService = catalogAccessService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _authStateNotifier = authStateNotifier;
        _navigationManager = navigationManager;
    }

    /// <summary>
    /// Who the cached snapshot was read for. Deliberately the same coarse pair AuthStateWatcher
    /// treats as an identity - a token refresh changes nothing about what the RPCs return, and
    /// dropping the cache on one would undo the point of holding it.
    /// </summary>
    private string CurrentIdentity =>
        $"{_authStateNotifier?.CurrentUserId ?? "anonymous"}|{(_authStateNotifier?.IsAdmin == true ? "admin" : "user")}";

    /// <summary>
    /// The page asking, with the query string and fragment cut off.
    /// </summary>
    /// <remarks>
    /// Read at the point of use rather than tracked through LocationChanged: by the time anything
    /// reads a snapshot the address bar already holds the page that wants it, so an event
    /// subscription would buy nothing and cost this service a lifetime to manage. Cutting the query
    /// is the whole mechanism - it is what makes a filter change free and a navigation honest.
    /// </remarks>
    private string CurrentPath
    {
        get
        {
            if (_navigationManager is null)
            {
                return string.Empty;
            }

            var relativePath = _navigationManager.ToBaseRelativePath(_navigationManager.Uri);
            var queryStart = relativePath.IndexOfAny(['?', '#']);
            return queryStart < 0 ? relativePath : relativePath[..queryStart];
        }
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

    /// <remarks>
    /// The rows are cached, and three things have to agree before a cached copy is handed back: the
    /// page asking has to be the same one it was read for, the visitor has to be the same, and the
    /// copy has to be younger than <see cref="SnapshotTtl"/>. The access check is never cached.
    /// <para>
    /// The reason to cache at all is that filtering, sorting and grouping happen client-side in
    /// <see cref="FranchiseService"/>, so a search term never reached Supabase - typing in the
    /// catalog's search box re-read four identical tables per keystroke.
    /// </para>
    /// <para>
    /// The reason to key on the path is that the saving is only wanted <em>within</em> a page. A
    /// filter, a sort and a page number are all query-string changes on one path, so those stay
    /// free; walking off to the calendar and back is a path change, and re-reads. That keeps the
    /// rule the app had before this cache existed - a navigation shows you current data - which
    /// matters most across two devices, where a stale catalog does not read as stale, it reads as
    /// the entry you just added on your phone having failed to save. A TTL alone could only make
    /// that unlikely; the path makes it impossible. What the TTL still does is bound a tab left
    /// parked on one path.
    /// </para>
    /// <para>
    /// And the reason to key on identity is that row-level security decides what those four reads
    /// return: an admin's snapshot must not be handed to the anonymous visitor the same browser
    /// turns into a moment later. Every page that reacts to a sign-out reloads through here, so
    /// keying on it means none of them has to remember to say so.
    /// </para>
    /// </remarks>
    public async Task<RepositorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _catalogAccessService.CanCurrentUserReadCatalogAsync(cancellationToken))
            {
                // Dropped rather than left to expire: access was refused, so nothing that was read
                // under the previous answer has any business staying in memory.
                InvalidateCachedReads();
                throw new CatalogAccessDeniedException();
            }

            var now = _timeProvider.GetUtcNow();

            if (_snapshot is not null
                && _snapshotExpiresAt > now
                && string.Equals(_snapshotPath, CurrentPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_snapshotIdentity, CurrentIdentity, StringComparison.Ordinal))
            {
                return _snapshot;
            }

            var animeRowsTask = _supabaseRestService.SelectAsync<AnimeEntryRow>("anime_entries", cancellationToken: cancellationToken);
            var catalogRowsTask = _supabaseRestService.SelectAsync<CatalogEntryRow>("catalog_entries", cancellationToken: cancellationToken);
            var relationRowsTask = _supabaseRestService.SelectAsync<AnimeRelationRow>("anime_relations", cancellationToken: cancellationToken);
            var franchiseRowsTask = _supabaseRestService.SelectAsync<FranchiseRow>("franchises", cancellationToken: cancellationToken);

            await Task.WhenAll(animeRowsTask, catalogRowsTask, relationRowsTask, franchiseRowsTask);

            // Safe to hand the same instance to every caller: RepositorySnapshot is a record over
            // IReadOnlyList, and nothing downstream writes to one.
            _snapshot = new RepositorySnapshot(
                animeRowsTask.Result.Select(Map).ToList(),
                catalogRowsTask.Result.Select(Map).ToList(),
                relationRowsTask.Result.Select(Map).ToList(),
                franchiseRowsTask.Result.Select(Map).ToList());

            // Stamped from the clock read before the request rather than after it, so a slow read
            // cannot extend its own lifetime.
            _snapshotExpiresAt = now + SnapshotTtl;
            _snapshotPath = CurrentPath;
            _snapshotIdentity = CurrentIdentity;

            return _snapshot;
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

    /// <summary>
    /// Drops both cached reads so the next one reflects a write that just happened.
    /// </summary>
    /// <remarks>
    /// One method rather than two, because the overlay is a projection of the snapshot: dropping the
    /// derived copy while the rows it was built from stay cached would rebuild it from the same data
    /// and change nothing. The TTLs are what bound how stale someone *else's* change can be; this is
    /// for the writer's own, who would otherwise be shown the version they just replaced.
    /// </remarks>
    public void InvalidateCachedReads()
    {
        _overlay = null;
        _overlayExpiresAt = DateTimeOffset.MinValue;
        _snapshot = null;
        _snapshotExpiresAt = DateTimeOffset.MinValue;
        _snapshotPath = null;
        _snapshotIdentity = null;
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
