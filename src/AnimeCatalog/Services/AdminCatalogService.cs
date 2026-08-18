using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.ViewModels;
using System.ComponentModel.DataAnnotations;

namespace AnimeCatalog.Services;

public sealed class AdminCatalogService
{
    private readonly ISupabaseRestService _supabaseRestService;
    private readonly IAniListService _aniListService;
    private readonly IAdminAuthorizationService _authService;
    private readonly ICatalogService _catalogService;

    public AdminCatalogService(
        ISupabaseRestService supabaseRestService,
        IAniListService aniListService,
        IAdminAuthorizationService authService,
        ICatalogService catalogService)
    {
        _supabaseRestService = supabaseRestService;
        _aniListService = aniListService;
        _authService = authService;
        _catalogService = catalogService;
    }

    public async Task EnsureAdminOrThrowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _authService.EnsureAdminAsync(cancellationToken))
        {
            throw new UnauthorizedAccessException("Admin access is required.");
        }
    }

    public async Task<IReadOnlyList<AniListMedia>> SearchAniListAsync(string query, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);
        return await _aniListService.SearchAnimeAsync(query, cancellationToken);
    }

    /// <summary>
    /// Maps every cataloged AniList id to its local anime entry id, so search results can be marked
    /// as already added before the user spends a click finding out.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, long>> GetCatalogedAniListIdsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        // Two columns of one table, rather than the four-table read GetSnapshotAsync does: this
        // runs on every visit to the add page and needs nothing else from the snapshot.
        var rows = await _supabaseRestService.SelectAsync<AnimeEntryRow>(
            "anime_entries",
            select: "id,anilist_id",
            cancellationToken: cancellationToken);

        // Grouped rather than ToDictionary: anilist_id is not guaranteed unique to this query, and a
        // duplicate row must not take down the whole page.
        return rows
            .GroupBy(row => row.AniListId)
            .ToDictionary(group => group.Key, group => group.First().Id);
    }

    public async Task<AnimeEditorModel> CreateDraftFromAniListAsync(int aniListId, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        var media = await _aniListService.GetAnimeByIdAsync(aniListId, cancellationToken)
            ?? throw new InvalidOperationException("AniList entry not found.");

        var existing = await FindByAniListIdAsync(aniListId, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Anime with AniList ID {aniListId} already exists (entry {existing.Id}).");
        }

        var snapshot = await _catalogService.GetSnapshotAsync(cancellationToken);

        return BuildDraft(media, snapshot);
    }

    /// <summary>
    /// Looks up an AniList id without rejecting it for already being in the catalog: reports the
    /// existing entry when there is one, a ready draft when there is not, and either way the anime
    /// relations marked against the catalog so a missing sequel is visible.
    /// </summary>
    public async Task<AniListInspectionViewModel> InspectAniListIdAsync(int aniListId, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        // The enriched query is required here: only it returns each relation node's type and format,
        // which is what separates a sequel from the source manga.
        var media = await _aniListService.GetEnrichedAnimeByIdAsync(aniListId, cancellationToken)
            ?? throw new InvalidOperationException("AniList entry not found.");

        var snapshot = await _catalogService.GetSnapshotAsync(cancellationToken);
        var existing = snapshot.AnimeEntries.FirstOrDefault(entry => entry.AniListId == aniListId);

        var franchise = existing?.FranchiseId is null
            ? null
            : snapshot.Franchises.FirstOrDefault(item => item.Id == existing.FranchiseId.Value);

        return new AniListInspectionViewModel
        {
            AniListId = aniListId,
            Title = media.Title.English ?? media.Title.Romaji ?? aniListId.ToString(),
            ExistingEntry = existing,
            ExistingFranchise = franchise,
            Relations = BuildRelationSuggestions(media, snapshot),
            Draft = existing is null ? BuildDraft(media, snapshot) : null
        };
    }

    private static AnimeEditorModel BuildDraft(AniListMedia media, RepositorySnapshot snapshot)
    {
        var suggestedFranchise = FindSuggestedFranchise(media, snapshot);

        return new AnimeEditorModel
        {
            AniListId = media.Id,
            TitleRomaji = media.Title.Romaji ?? media.Title.English ?? "Untitled",
            TitleEnglish = media.Title.English,
            TitleNative = media.Title.Native,
            CoverUrl = media.CoverImage?.ExtraLarge ?? media.CoverImage?.Large,
            Format = media.Format,
            Season = media.Season,
            SeasonYear = media.SeasonYear,
            Episodes = media.Episodes,
            StartDate = media.StartDate?.ToDateOnly(),
            EndDate = media.EndDate?.ToDateOnly(),
            Status = CatalogStatus.Completed,
            // Mirrors AnimeEditorForm.HandleStatusChangedAsync, which fills this in when the user
            // switches to Completed. A default never fires that handler, so do it here or the
            // draft opens as Completed with 0 episodes watched. Null for currently-airing shows.
            EpisodesWatched = media.Episodes ?? 0,
            FranchiseId = suggestedFranchise?.Id,
            FranchiseAssignmentMode = suggestedFranchise is null ? FranchiseAssignmentMode.None : FranchiseAssignmentMode.Existing,
            SuggestedFranchiseTitle = suggestedFranchise?.Title,
            SuggestedNewFranchiseTitle = FranchiseTitleSuggester.Build(media.Title.English, media.Title.Romaji),
            RelatedSuggestions = [.. BuildRelationSuggestions(media, snapshot)]
        };
    }

    /// <summary>
    /// Anime relations only, each carrying its local entry id when the catalog already has it.
    /// </summary>
    private static IReadOnlyList<RelatedAnimeSuggestion> BuildRelationSuggestions(AniListMedia media, RepositorySnapshot snapshot)
    {
        var entriesByAniListId = snapshot.AnimeEntries
            .GroupBy(entry => entry.AniListId)
            .ToDictionary(group => group.Key, group => group.First());

        return (media.Relations?.Edges ?? [])
            .Where(edge => edge.Node is not null)
            .Where(edge => AnimeRelationRules.IsRenderable(edge.RelationType, edge.Node!.Type, edge.Node.Format))
            .GroupBy(edge => edge.Node!.Id)
            .Select(group => group.First())
            .Select(edge => new RelatedAnimeSuggestion
            {
                AniListId = edge.Node!.Id,
                Title = edge.Node.Title.English ?? edge.Node.Title.Romaji ?? edge.Node.Id.ToString(),
                CoverUrl = edge.Node.CoverImage?.BestUrl,
                Format = edge.Node.Format,
                SeasonYear = edge.Node.SeasonYear,
                RelationType = edge.RelationType ?? "OTHER",
                LocalAnimeEntryId = entriesByAniListId.GetValueOrDefault(edge.Node.Id)?.Id
            })
            .ToList();
    }

    public async Task<long> SaveAsync(AnimeEditorModel model, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);
        Validator.ValidateObject(model, new ValidationContext(model), validateAllProperties: true);

        var franchiseId = await ResolveFranchiseIdAsync(model, cancellationToken);
        var targetAnimeEntryId = model.AnimeEntryId;
        var wasCreated = false;

        if (targetAnimeEntryId is null)
        {
            var duplicate = await FindByAniListIdAsync(model.AniListId, cancellationToken);
            if (duplicate is not null)
            {
                targetAnimeEntryId = duplicate.Id;
            }
            else
            {
                var createdAnime = await _supabaseRestService.InsertSingleAsync<AnimeEntryRow>("anime_entries", new
                {
                    anilist_id = model.AniListId,
                    franchise_id = franchiseId,
                    title_romaji = model.TitleRomaji,
                    title_english = model.TitleEnglish,
                    title_native = model.TitleNative,
                    cover_url = model.CoverUrl,
                    format = model.Format,
                    season = model.Season,
                    season_year = model.SeasonYear,
                    episodes = model.Episodes,
                    start_date = model.StartDate,
                    end_date = model.EndDate,
                    season_number = model.SeasonNumber,
                    part_number = model.PartNumber,
                    display_order = model.DisplayOrder
                }, cancellationToken) ?? throw new InvalidOperationException("Anime insert returned no data.");

                targetAnimeEntryId = createdAnime.Id;
                wasCreated = true;
            }
        }

        if (!wasCreated)
        {
            await _supabaseRestService.UpdateSingleAsync<Dictionary<string, object>>("anime_entries", new Dictionary<string, string>
            {
                ["id"] = $"eq.{targetAnimeEntryId!.Value}"
            }, new
            {
                franchise_id = franchiseId,
                title_romaji = model.TitleRomaji,
                title_english = model.TitleEnglish,
                title_native = model.TitleNative,
                cover_url = model.CoverUrl,
                format = model.Format,
                season = model.Season,
                season_year = model.SeasonYear,
                episodes = model.Episodes,
                start_date = model.StartDate,
                end_date = model.EndDate,
                season_number = model.SeasonNumber,
                part_number = model.PartNumber,
                display_order = model.DisplayOrder
            }, cancellationToken);
        }

        await _supabaseRestService.UpsertSingleAsync<CatalogEntryRow>("catalog_entries", new
        {
            anime_entry_id = targetAnimeEntryId.Value,
            status = model.Status.ToApiValue(),
            score = model.Score,
            episodes_watched = model.EpisodesWatched,
            notes = model.Notes,
            started_at = model.StartedAt,
            completed_at = model.CompletedAt
        }, "anime_entry_id", cancellationToken);

        var verifiedCatalogEntry = await _supabaseRestService.SelectSingleAsync<CatalogEntryRow>(
            "catalog_entries",
            new Dictionary<string, string>
            {
                ["anime_entry_id"] = $"eq.{targetAnimeEntryId.Value}"
            },
            cancellationToken: cancellationToken);

        if (verifiedCatalogEntry is null)
        {
            throw new InvalidOperationException($"Catalog entry for anime_entry_id={targetAnimeEntryId.Value} was not created.");
        }

        await ReplaceRelationsAsync(targetAnimeEntryId.Value, model.AniListId, cancellationToken);
        return targetAnimeEntryId.Value;
    }

    /// <summary>
    /// Updates only the catalog row for an entry — used by the inline status/score controls on the
    /// anime page.
    /// </summary>
    /// <remarks>
    /// Deliberately not routed through <see cref="SaveAsync"/>: that revalidates the whole editor
    /// model and calls <c>ReplaceRelationsAsync</c>, which round-trips AniList. Clicking a status pill
    /// should not cost an AniList request or risk touching anime_relations.
    /// </remarks>
    public async Task UpdateCatalogEntryAsync(
        long animeEntryId,
        CatalogStatus status,
        decimal? score,
        int episodesWatched,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        if (episodesWatched < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(episodesWatched), episodesWatched, "Episodes watched cannot be negative.");
        }

        if (score is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Score must be between 0 and 10.");
        }

        await _supabaseRestService.UpsertSingleAsync<CatalogEntryRow>("catalog_entries", new
        {
            anime_entry_id = animeEntryId,
            status = status.ToApiValue(),
            score,
            episodes_watched = episodesWatched
        }, "anime_entry_id", cancellationToken);
    }

    public async Task DeleteAsync(long animeEntryId, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);
        await _supabaseRestService.DeleteAsync("anime_entries", new Dictionary<string, string>
        {
            ["id"] = $"eq.{animeEntryId}"
        }, cancellationToken);
    }

    public async Task RefreshMetadataAsync(long animeEntryId, CancellationToken cancellationToken = default)
    {
        await EnsureAdminOrThrowAsync(cancellationToken);

        var editor = await _catalogService.GetEditorModelAsync(animeEntryId, cancellationToken)
            ?? throw new InvalidOperationException("Anime entry not found.");
        var metadata = await _aniListService.GetAnimeByIdAsync(editor.AniListId, cancellationToken)
            ?? throw new InvalidOperationException("AniList metadata not found.");

        editor.TitleRomaji = metadata.Title.Romaji ?? editor.TitleRomaji;
        editor.TitleEnglish = metadata.Title.English;
        editor.TitleNative = metadata.Title.Native;
        editor.CoverUrl = metadata.CoverImage?.ExtraLarge ?? metadata.CoverImage?.Large;
        editor.Format = metadata.Format;
        editor.Season = metadata.Season;
        editor.SeasonYear = metadata.SeasonYear;
        editor.Episodes = metadata.Episodes;
        editor.StartDate = metadata.StartDate?.ToDateOnly();
        editor.EndDate = metadata.EndDate?.ToDateOnly();

        await SaveAsync(editor, cancellationToken);
    }

    private async Task<long?> ResolveFranchiseIdAsync(AnimeEditorModel model, CancellationToken cancellationToken)
    {
        return model.FranchiseAssignmentMode switch
        {
            FranchiseAssignmentMode.None => null,
            FranchiseAssignmentMode.Existing => model.FranchiseId,
            FranchiseAssignmentMode.CreateNew => await CreateFranchiseAsync(model, cancellationToken),
            _ => null
        };
    }

    private async Task<long> CreateFranchiseAsync(AnimeEditorModel model, CancellationToken cancellationToken)
    {
        var title = string.IsNullOrWhiteSpace(model.NewFranchiseTitle)
            ? model.TitleEnglish ?? model.TitleRomaji
            : model.NewFranchiseTitle.Trim();

        var slugBase = SlugGenerator.Generate(title);
        var existing = await _supabaseRestService.SelectAsync<Dictionary<string, object>>("franchises", new Dictionary<string, string>
        {
            ["slug"] = $"eq.{slugBase}"
        }, cancellationToken: cancellationToken);

        var slug = existing.Count == 0 ? slugBase : $"{slugBase}-{Guid.NewGuid():N}"[..Math.Min(8, slugBase.Length + 8)];

        var createdFranchise = await _supabaseRestService.InsertSingleAsync<FranchiseRow>("franchises", new
        {
            title,
            slug,
            cover_url = string.IsNullOrWhiteSpace(model.NewFranchiseCoverUrl) ? model.CoverUrl : model.NewFranchiseCoverUrl,
            description = model.NewFranchiseDescription
        }, cancellationToken) ?? throw new InvalidOperationException("Franchise insert returned no data.");

        return createdFranchise.Id;
    }

    private async Task<AnimeEntry?> FindByAniListIdAsync(int aniListId, CancellationToken cancellationToken)
    {
        var snapshot = await _catalogService.GetSnapshotAsync(cancellationToken);
        return snapshot.AnimeEntries.SingleOrDefault(item => item.AniListId == aniListId);
    }

    // Takes the snapshot rather than fetching its own, so building a draft costs one snapshot read.
    private static Franchise? FindSuggestedFranchise(AniListMedia media, RepositorySnapshot snapshot)
    {
        var relatedAniListIds = media.Relations?.Edges
            .Where(edge => edge.Node is not null)
            .Select(edge => edge.Node!.Id)
            .ToHashSet()
            ?? [];

        if (relatedAniListIds.Count == 0)
        {
            return null;
        }

        var bestMatch = snapshot.AnimeEntries
            .Where(item => item.FranchiseId is not null && relatedAniListIds.Contains(item.AniListId))
            .GroupBy(item => item.FranchiseId!.Value)
            .Select(group => new
            {
                FranchiseId = group.Key,
                MatchCount = group.Count()
            })
            .OrderByDescending(item => item.MatchCount)
            .ThenBy(item => item.FranchiseId)
            .FirstOrDefault();

        return bestMatch is null
            ? null
            : snapshot.Franchises.FirstOrDefault(item => item.Id == bestMatch.FranchiseId);
    }

    private async Task ReplaceRelationsAsync(long sourceAnimeId, int aniListId, CancellationToken cancellationToken)
    {
        var metadata = await _aniListService.GetAnimeByIdAsync(aniListId, cancellationToken);
        if (metadata is null)
        {
            return;
        }

        await _supabaseRestService.DeleteAsync("anime_relations", new Dictionary<string, string>
        {
            ["source_anime_id"] = $"eq.{sourceAnimeId}"
        }, cancellationToken);

        var payload = metadata.Relations?.Edges
            .Where(edge => edge.Node is not null && !string.IsNullOrWhiteSpace(edge.RelationType))
            .Select(edge => new
            {
                source_anime_id = sourceAnimeId,
                target_anilist_id = edge.Node!.Id,
                relation_type = edge.RelationType
            })
            .Cast<object>()
            .ToList();

        if (payload is { Count: > 0 })
        {
            await _supabaseRestService.InsertManyAsync<Dictionary<string, object>>("anime_relations", payload, cancellationToken);
        }
    }
}
