using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

public sealed class FranchiseService
{
    public IReadOnlyList<FranchiseSummaryViewModel> BuildCatalog(
        IReadOnlyList<AnimeEntry> animeEntries,
        IReadOnlyList<CatalogEntry> catalogEntries,
        IReadOnlyList<AnimeRelation> relations,
        IReadOnlyList<Franchise> franchises,
        CatalogFilters filters)
    {
        var catalogByAnimeId = catalogEntries.ToDictionary(item => item.AnimeEntryId);
        var relationsByAnimeId = relations.GroupBy(item => item.SourceAnimeId).ToDictionary(group => group.Key, group => (IReadOnlyList<AnimeRelation>)group.ToList());
        var franchiseById = franchises.ToDictionary(item => item.Id);

        var items = animeEntries
            .Select(item =>
            {
                if (!catalogByAnimeId.TryGetValue(item.Id, out var catalogEntry))
                {
                    throw new InvalidOperationException($"Catalog entry for anime_entry_id={item.Id} is missing.");
                }

                return new AnimeListItemViewModel
                {
                    AnimeEntry = item,
                    CatalogEntry = catalogEntry,
                    Franchise = item.FranchiseId is not null && franchiseById.TryGetValue(item.FranchiseId.Value, out var franchise) ? franchise : null,
                    Relations = relationsByAnimeId.GetValueOrDefault(item.Id, [])
                };
            })
            .ToList();

        var normalizedQuery = filters.Query.Trim();

        var grouped = items
            .GroupBy(item => item.Franchise?.Id ?? -item.AnimeEntry.Id)
            .Select(group =>
            {
                var orderedEntries = group
                    .OrderBy(entry => entry.AnimeEntry.DisplayOrder)
                    .ThenBy(entry => entry.AnimeEntry.SeasonYear)
                    .ThenBy(entry => entry.PrimaryTitle)
                    .ToList();

                var visibleEntries = orderedEntries
                    .Where(entry => MatchesFilters(entry, normalizedQuery, filters.Status))
                    .ToList();

                if (visibleEntries.Count == 0)
                {
                    return null;
                }

                var sourceFranchise = orderedEntries[0].Franchise;
                var scoredEntries = orderedEntries.Where(entry => entry.CatalogEntry.Score is not null).ToList();

                return new FranchiseSummaryViewModel
                {
                    FranchiseId = sourceFranchise?.Id,
                    Slug = sourceFranchise?.Slug,
                    Title = sourceFranchise?.Title ?? orderedEntries[0].PrimaryTitle,
                    CoverUrl = sourceFranchise?.CoverUrl ?? orderedEntries[0].AnimeEntry.CoverUrl,
                    EntryCount = orderedEntries.Count,
                    CompletedCount = orderedEntries.Count(entry => entry.CatalogEntry.Status == CatalogStatus.Completed),
                    AverageScore = scoredEntries.Count == 0 ? null : Math.Round(scoredEntries.Average(entry => entry.CatalogEntry.Score!.Value), 1),
                    IsWatching = orderedEntries.Any(entry => entry.CatalogEntry.Status == CatalogStatus.Watching),
                    Entries = orderedEntries,
                    VisibleEntries = visibleEntries
                };
            })
            .Where(item => item is not null)
            .Cast<FranchiseSummaryViewModel>()
            .ToList();

        return ApplySort(grouped, filters.Sort);
    }

    public FranchiseDetailsViewModel BuildFranchiseDetails(
        Franchise franchise,
        IReadOnlyList<AnimeEntry> animeEntries,
        IReadOnlyList<CatalogEntry> catalogEntries,
        IReadOnlyList<AnimeRelation> relations)
    {
        var franchiseEntries = animeEntries.Where(item => item.FranchiseId == franchise.Id).ToList();

        // A franchise can legitimately end up with no entries: anime_entries.franchise_id is
        // "on delete set null", so deleting the last anime empties the franchise instead of removing
        // it. BuildCatalog returns nothing for an empty input, so this must not call .Single().
        var summary = BuildCatalog(franchiseEntries, catalogEntries, relations, [franchise], new CatalogFilters())
            .SingleOrDefault()
            ?? new FranchiseSummaryViewModel
            {
                FranchiseId = franchise.Id,
                Slug = franchise.Slug,
                Title = franchise.Title,
                CoverUrl = franchise.CoverUrl
            };

        return new FranchiseDetailsViewModel
        {
            Franchise = franchise,
            Summary = summary,
            Stats = BuildFranchiseStats(summary),
            Timeline = BuildTimeline(summary.Entries),
            RelatedOutsideFranchise = ResolveRelatedOutsideFranchise(summary.Entries, animeEntries, catalogEntries)
        };
    }

    public AnimeDetailsViewModel BuildAnimeDetails(
        AnimeEntry animeEntry,
        CatalogEntry catalogEntry,
        IReadOnlyList<AnimeRelation> relations,
        Franchise? franchise,
        IReadOnlyList<AnimeEntry> allAnimeEntries,
        IReadOnlyList<CatalogEntry> allCatalogEntries)
    {
        var resolved = ResolveRelations(
            relations.Where(item => item.SourceAnimeId == animeEntry.Id).ToList(),
            null,
            allAnimeEntries,
            allCatalogEntries);

        return new AnimeDetailsViewModel
        {
            AnimeEntry = animeEntry,
            CatalogEntry = catalogEntry,
            Franchise = franchise,
            Relations = AppendFranchiseSiblings(resolved, animeEntry, franchise, allAnimeEntries, allCatalogEntries)
        };
    }

    /// <summary>
    /// Adds the other entries of this anime's franchise to its relation list.
    /// </summary>
    /// <remarks>
    /// AniList relations are only ever one hop, so a season two can be invisible from season one:
    /// Made in Abyss links S1 to the <em>movie</em>, and only the movie links on to S2. The franchise
    /// grouping is admin-curated and already spans that gap, so siblings fill it with no extra API
    /// call and no dependency on AniList being reachable.
    /// </remarks>
    public IReadOnlyList<RelationLinkViewModel> AppendFranchiseSiblings(
        IReadOnlyList<RelationLinkViewModel> relations,
        AnimeEntry animeEntry,
        Franchise? franchise,
        IReadOnlyList<AnimeEntry> allAnimeEntries,
        IReadOnlyList<CatalogEntry> allCatalogEntries)
    {
        if (franchise is null)
        {
            return relations;
        }

        // A sibling that AniList also reports as a relation keeps that relation's label: "Side story"
        // says more than "Same franchise".
        var alreadyRelated = relations.Select(relation => relation.TargetAniListId).ToHashSet();

        var catalogByAnimeId = allCatalogEntries
            .GroupBy(entry => entry.AnimeEntryId)
            .ToDictionary(group => group.Key, group => group.First());

        var siblings = allAnimeEntries
            .Where(entry => entry.FranchiseId == franchise.Id
                         && entry.Id != animeEntry.Id
                         && !alreadyRelated.Contains(entry.AniListId)
                         && !IsMusicFormat(entry.Format))
            .Select(entry => new RelationLinkViewModel
            {
                RelationType = SameFranchiseRelationType,
                TargetAniListId = entry.AniListId,
                LocalAnimeEntryId = entry.Id,
                Title = entry.TitleEnglish ?? entry.TitleRomaji,
                CoverUrl = entry.CoverUrl,
                Format = entry.Format,
                SeasonYear = entry.SeasonYear,
                CatalogStatus = catalogByAnimeId.GetValueOrDefault(entry.Id)?.Status,
                IsConfirmedAnime = true
            });

        return OrderRelations(relations.Concat(siblings)).ToList();
    }

    /// <summary>
    /// Aggregates that need only Supabase data, so the franchise page's stats, completion meter and
    /// timeline are correct on first paint and stay correct if AniList never answers.
    /// </summary>
    public FranchiseStatsViewModel BuildFranchiseStats(FranchiseSummaryViewModel summary)
    {
        var entries = summary.Entries;
        var scores = entries.Where(entry => entry.CatalogEntry.Score is not null)
            .Select(entry => entry.CatalogEntry.Score!.Value)
            .ToList();

        var years = entries
            .Select(entry => entry.AnimeEntry.SeasonYear ?? entry.AnimeEntry.StartDate?.Year)
            .Where(year => year is not null)
            .Select(year => year!.Value)
            .ToList();

        var startedDates = entries.Where(entry => entry.CatalogEntry.StartedAt is not null)
            .Select(entry => entry.CatalogEntry.StartedAt!.Value)
            .ToList();

        var completedDates = entries.Where(entry => entry.CatalogEntry.CompletedAt is not null)
            .Select(entry => entry.CatalogEntry.CompletedAt!.Value)
            .ToList();

        var episodes = SumEpisodes(entries);

        return new FranchiseStatsViewModel
        {
            EntryCount = entries.Count,
            StatusBreakdown = BuildStatusBreakdown(entries),
            EpisodesWatched = episodes.Watched,
            EpisodesTotal = episodes.Total,
            HasUnknownEpisodeCounts = episodes.HasUnknown,
            CompletedCount = entries.Count(entry => entry.CatalogEntry.Status == CatalogStatus.Completed),
            ScoredCount = scores.Count,
            AverageScore = scores.Count == 0 ? null : Math.Round(scores.Average(), 1),
            HighestScore = scores.Count == 0 ? null : scores.Max(),
            LowestScore = scores.Count == 0 ? null : scores.Min(),
            FirstYear = years.Count == 0 ? null : years.Min(),
            LastYear = years.Count == 0 ? null : years.Max(),
            FirstStartedAt = startedDates.Count == 0 ? null : startedDates.Min(),
            LastCompletedAt = completedDates.Count == 0 ? null : completedDates.Max(),
            IsWatching = entries.Any(entry => entry.CatalogEntry.Status == CatalogStatus.Watching)
        };
    }

    /// <summary>Buckets entries by release year, ascending, with unknown years last.</summary>
    public IReadOnlyList<FranchiseTimelineGroup> BuildTimeline(IReadOnlyList<AnimeListItemViewModel> entries)
    {
        return entries
            .GroupBy(entry => entry.AnimeEntry.SeasonYear ?? entry.AnimeEntry.StartDate?.Year)
            .OrderBy(group => group.Key is null)
            .ThenBy(group => group.Key)
            .Select(group => new FranchiseTimelineGroup
            {
                Year = group.Key,
                Entries = group
                    .OrderBy(entry => MediaDisplayFormatter.SeasonSortKey(entry.AnimeEntry.Season))
                    .ThenBy(entry => entry.AnimeEntry.DisplayOrder)
                    .ThenBy(entry => entry.PrimaryTitle)
                    .ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Turns raw <c>anime_relations</c> rows into linkable cards. Targets already in the catalog
    /// become internal links; the rest fall back to the live AniList edge for a title and cover and
    /// link outward. Live data is optional so this works before enrichment lands.
    /// </summary>
    public IReadOnlyList<RelationLinkViewModel> ResolveRelations(
        IReadOnlyList<AnimeRelation> relations,
        AniListMedia? liveMedia,
        IReadOnlyList<AnimeEntry> allAnimeEntries,
        IReadOnlyList<CatalogEntry> allCatalogEntries)
    {
        if (relations.Count == 0)
        {
            return [];
        }

        // anilist_id is unique in the schema, but GroupBy keeps this total for arbitrary test input
        // instead of throwing on a duplicate.
        var entriesByAniListId = allAnimeEntries
            .GroupBy(entry => entry.AniListId)
            .ToDictionary(group => group.Key, group => group.First());

        var catalogByAnimeId = allCatalogEntries
            .GroupBy(entry => entry.AnimeEntryId)
            .ToDictionary(group => group.Key, group => group.First());

        var liveByAniListId = (liveMedia?.Relations?.Edges ?? [])
            .Where(edge => edge.Node is not null)
            .GroupBy(edge => edge.Node!.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var resolved = relations
            // CHARACTER and OTHER are the vaguest links AniList models — usually a cameo — so they are
            // dropped outright rather than competing with sequels for space.
            .Where(relation => !IsWeakRelation(relation.RelationType))
            .GroupBy(relation => (relation.TargetAniListId, relation.RelationType))
            .Select(group => group.First())
            .Select(relation =>
            {
                var live = liveByAniListId.GetValueOrDefault(relation.TargetAniListId);
                var node = live?.Node;
                var local = entriesByAniListId.GetValueOrDefault(relation.TargetAniListId);

                var title = local is not null
                    ? local.TitleEnglish ?? local.TitleRomaji
                    : node?.Title.English ?? node?.Title.Romaji ?? node?.Title.Native
                      ?? $"AniList #{relation.TargetAniListId}";

                return new RelationLinkViewModel
                {
                    RelationType = relation.RelationType,
                    TargetAniListId = relation.TargetAniListId,
                    LocalAnimeEntryId = local?.Id,
                    Title = title,
                    CoverUrl = local?.CoverUrl ?? node?.CoverImage?.BestUrl,
                    Format = local?.Format ?? node?.Format,
                    SeasonYear = local?.SeasonYear ?? node?.SeasonYear,
                    CatalogStatus = local is not null && catalogByAnimeId.TryGetValue(local.Id, out var catalogEntry)
                        ? catalogEntry.Status
                        : null,
                    SiteUrl = node?.SiteUrl,
                    // In-catalog rows are anime by construction; out-of-catalog rows need AniList to say so.
                    IsConfirmedAnime = local is not null
                        ? !IsMusicFormat(local.Format)
                        : node is not null && IsAnimeType(node.Type) && !IsMusicFormat(node.Format)
                };
            })
            // A target AniList has classified as something this catalog does not hold is discarded;
            // one it has not classified yet survives as unconfirmed so enrichment can resolve it.
            .Where(relation => relation.IsConfirmedAnime || IsUnresolved(relation, liveByAniListId));

        return OrderRelations(resolved).ToList();
    }

    private static IOrderedEnumerable<RelationLinkViewModel> OrderRelations(IEnumerable<RelationLinkViewModel> relations) =>
        relations
            .OrderBy(relation => RelationSortKey(relation.RelationType))
            .ThenBy(relation => relation.SeasonYear ?? int.MaxValue)
            .ThenBy(relation => relation.Title);

    private static bool IsUnresolved(
        RelationLinkViewModel relation,
        Dictionary<int, AniListRelationEdge> liveByAniListId) =>
        !relation.IsInCatalog && !liveByAniListId.ContainsKey(relation.TargetAniListId);

    // Delegated to AnimeRelationRules so the admin add flow applies exactly the same rules.
    private static bool IsWeakRelation(string? relationType) => AnimeRelationRules.IsWeakRelation(relationType);

    private static bool IsMusicFormat(string? format) => AnimeRelationRules.IsMusicFormat(format);

    private static bool IsAnimeType(string? type) => AnimeRelationRules.IsAnimeType(type);

    /// <summary>
    /// Relation targets of this franchise's entries that are not themselves part of the franchise.
    /// </summary>
    public IReadOnlyList<RelationLinkViewModel> ResolveRelatedOutsideFranchise(
        IReadOnlyList<AnimeListItemViewModel> franchiseEntries,
        IReadOnlyList<AnimeEntry> allAnimeEntries,
        IReadOnlyList<CatalogEntry> allCatalogEntries)
    {
        if (franchiseEntries.Count == 0)
        {
            return [];
        }

        var insideAniListIds = franchiseEntries.Select(entry => entry.AnimeEntry.AniListId).ToHashSet();

        var outside = franchiseEntries
            .SelectMany(entry => entry.Relations)
            .Where(relation => !insideAniListIds.Contains(relation.TargetAniListId))
            .ToList();

        return ResolveRelations(outside, null, allAnimeEntries, allCatalogEntries)
            .GroupBy(relation => relation.TargetAniListId)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// Fills in titles and covers for relation targets that are not in the local catalog, once the
    /// live AniList payload arrives.
    /// </summary>
    /// <remarks>
    /// In-catalog targets are already complete from Supabase and are left untouched, so this needs no
    /// snapshot and can run on whatever the page already rendered.
    /// </remarks>
    public IReadOnlyList<RelationLinkViewModel> MergeLiveRelationData(
        IReadOnlyList<RelationLinkViewModel> relations,
        AniListMedia liveMedia) => MergeLiveRelationData(relations, [liveMedia]);

    /// <summary>
    /// Same as the single-media overload, but pools the relation edges of several media. The franchise
    /// page needs this: its "related outside this franchise" set is drawn from every entry at once.
    /// </summary>
    public IReadOnlyList<RelationLinkViewModel> MergeLiveRelationData(
        IReadOnlyList<RelationLinkViewModel> relations,
        IReadOnlyCollection<AniListMedia> liveMedia)
    {
        var liveByAniListId = liveMedia
            .SelectMany(media => media.Relations?.Edges ?? [])
            .Where(edge => edge.Node is not null)
            .GroupBy(edge => edge.Node!.Id)
            .ToDictionary(group => group.Key, group => group.First().Node!);

        if (liveByAniListId.Count == 0)
        {
            return relations;
        }

        return relations
            .Select(relation =>
            {
                if (relation.IsConfirmedAnime || !liveByAniListId.TryGetValue(relation.TargetAniListId, out var node))
                {
                    return relation;
                }

                // Now that AniList has classified it, anything this catalog does not hold is discarded
                // by the filter below rather than lingering as an unresolved row.
                if (!IsAnimeType(node.Type) || IsMusicFormat(node.Format))
                {
                    return null;
                }

                var title = node.Title.English ?? node.Title.Romaji ?? node.Title.Native;

                return new RelationLinkViewModel
                {
                    RelationType = relation.RelationType,
                    TargetAniListId = relation.TargetAniListId,
                    LocalAnimeEntryId = relation.LocalAnimeEntryId,
                    Title = string.IsNullOrWhiteSpace(title) ? relation.Title : title,
                    CoverUrl = node.CoverImage?.BestUrl ?? relation.CoverUrl,
                    Format = node.Format ?? relation.Format,
                    SeasonYear = node.SeasonYear ?? relation.SeasonYear,
                    CatalogStatus = relation.CatalogStatus,
                    SiteUrl = node.SiteUrl ?? relation.SiteUrl,
                    IsConfirmedAnime = true
                };
            })
            .OfType<RelationLinkViewModel>()
            .ToList();
    }

    /// <summary>
    /// Builds the AniList-only enrichment for one anime: sanitized synopsis, tags split by spoiler,
    /// studios split by role, notable rankings.
    /// </summary>
    public AnimeEnrichmentViewModel BuildAnimeEnrichment(AniListMedia media, int? localEpisodeCount)
    {
        var studios = media.Studios?.Edges ?? [];
        var episodes = localEpisodeCount ?? media.Episodes;

        return new AnimeEnrichmentViewModel
        {
            Media = media,
            Description = AniListDescriptionSanitizer.Sanitize(media.Description),
            Tags = media.Tags
                .Where(tag => !tag.IsSpoiler && !string.IsNullOrWhiteSpace(tag.Name))
                .OrderByDescending(tag => tag.Rank ?? 0)
                .ThenBy(tag => tag.Name)
                .Take(18)
                .ToList(),
            SpoilerTags = media.Tags
                .Where(tag => tag.IsSpoiler && !string.IsNullOrWhiteSpace(tag.Name))
                .OrderByDescending(tag => tag.Rank ?? 0)
                .ToList(),
            MainStudios = studios.Where(edge => edge.IsMain && edge.Node is not null)
                .Select(edge => edge.Node!)
                .DistinctBy(studio => studio.Id)
                .ToList(),
            Producers = studios.Where(edge => !edge.IsMain && edge.Node is not null)
                .Select(edge => edge.Node!)
                .DistinctBy(studio => studio.Id)
                .ToList(),
            // All-time placements first: "#12 most popular all time" beats "#3 of Spring 2016".
            Rankings = media.Rankings
                .OrderByDescending(ranking => ranking.AllTime == true)
                .ThenBy(ranking => ranking.Rank)
                .Take(4)
                .ToList(),
            TotalRuntimeMinutes = episodes is > 0 && media.Duration is > 0
                ? episodes.Value * media.Duration.Value
                : null
        };
    }

    /// <summary>
    /// Rolls AniList data up across a whole franchise. Entries that failed to load are simply absent
    /// from <paramref name="mediaByAniListId"/> and contribute nothing, so a partial result is valid.
    /// </summary>
    public FranchiseEnrichmentViewModel BuildFranchiseEnrichment(
        IReadOnlyList<AnimeListItemViewModel> entries,
        IReadOnlyDictionary<int, AniListMedia> mediaByAniListId)
    {
        var loaded = entries
            .Select(entry => new
            {
                Entry = entry,
                Media = mediaByAniListId.GetValueOrDefault(entry.AnimeEntry.AniListId)
            })
            .Where(pair => pair.Media is not null)
            .Select(pair => new { pair.Entry, Media = pair.Media! })
            .ToList();

        if (loaded.Count == 0)
        {
            return new FranchiseEnrichmentViewModel
            {
                ByAniListId = mediaByAniListId,
                EntryCount = entries.Count,
                LoadedCount = 0
            };
        }

        var scores = loaded.Where(pair => pair.Media.AverageScore is not null)
            .Select(pair => pair.Media.AverageScore!.Value)
            .ToList();

        return new FranchiseEnrichmentViewModel
        {
            ByAniListId = mediaByAniListId,
            EntryCount = entries.Count,
            LoadedCount = loaded.Count,
            Genres = Rollup(loaded.SelectMany(pair => pair.Media.Genres)),
            AniListAverageScore = scores.Count == 0 ? null : (int)Math.Round(scores.Average()),
            // The most popular entry's banner is the flagship season's art rather than an OVA's.
            BannerUrl = loaded
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Media.BannerImage))
                .OrderByDescending(pair => pair.Media.Popularity ?? 0)
                .Select(pair => pair.Media.BannerImage)
                .FirstOrDefault()
        };
    }

    private static IReadOnlyList<LabelCount> Rollup(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new LabelCount(group.First().Trim(), group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Synthetic relation type for franchise siblings. Not an AniList <c>MediaRelation</c> value — this
    /// app adds it so curated grouping can travel through the same rendering path as real relations.
    /// </summary>
    public const string SameFranchiseRelationType = "SAME_FRANCHISE";

    // Reading order for a franchise: entries you have grouped yourself, then what it came from, then
    // backwards, then forwards, then the periphery. Unknown types sort just before OTHER.
    private static int RelationSortKey(string? relationType) => relationType?.Trim().ToUpperInvariant() switch
    {
        SameFranchiseRelationType => -1,
        "SOURCE" => 0,
        "PARENT" => 1,
        "PREQUEL" => 2,
        "SEQUEL" => 3,
        "SIDE_STORY" => 4,
        "ALTERNATIVE" => 5,
        "SPIN_OFF" => 6,
        "SUMMARY" => 7,
        "COMPILATION" => 8,
        "CONTAINS" => 9,
        "ADAPTATION" => 10,
        "CHARACTER" => 11,
        "OTHER" => 13,
        _ => 12
    };

    /// <summary>
    /// Whole-catalog aggregates for the home page. <paramref name="now"/> is passed in rather than
    /// read from the clock so the activity windows stay testable, matching how
    /// <see cref="CatalogTransferService"/> takes its export timestamp.
    /// </summary>
    public HomeSummaryViewModel BuildHomeSummary(
        IReadOnlyList<FranchiseSummaryViewModel> franchises,
        DateTimeOffset now)
    {
        var allEntries = franchises.SelectMany(item => item.Entries).ToList();

        // BuildCatalog emits a singleton pseudo-franchise per ungrouped anime, keyed by the negated
        // anime id and carrying a null FranchiseId and Slug. Those are entries, not franchises, so
        // every franchise-shaped number here and every slug-based link has to filter them out.
        var realFranchises = franchises.Where(item => item.FranchiseId is not null).ToList();

        var scores = allEntries
            .Where(item => item.CatalogEntry.Score is not null)
            .Select(item => item.CatalogEntry.Score!.Value)
            .ToList();

        var episodes = SumEpisodes(allEntries);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var thirtyDaysAgo = today.AddDays(-30);
        var completedDates = allEntries
            .Where(item => item.CatalogEntry.CompletedAt is not null)
            .Select(item => item.CatalogEntry.CompletedAt!.Value)
            .ToList();

        // Most recently touched first: that is what "continue watching" means in practice, and it is
        // the same ordering the old CurrentlyWatching list used.
        var watching = allEntries
            .Where(item => item.CatalogEntry.Status == CatalogStatus.Watching)
            .OrderByDescending(item => item.CatalogEntry.UpdatedAt)
            .ThenBy(item => item.PrimaryTitle)
            .ToList();

        return new HomeSummaryViewModel
        {
            TotalEntries = allEntries.Count,
            FranchiseCount = realFranchises.Count,
            StandaloneCount = franchises.Count - realFranchises.Count,
            CompletedFranchises = realFranchises.Count(item => item.EntryCount > 0 && item.CompletedCount == item.EntryCount),
            StatusBreakdown = BuildStatusBreakdown(allEntries),
            EpisodesWatched = episodes.Watched,
            EpisodesTotal = episodes.Total,
            HasUnknownEpisodeCounts = episodes.HasUnknown,
            AverageScore = scores.Count == 0 ? null : Math.Round(scores.Average(), 1),
            ScoredCount = scores.Count,
            HighestScore = scores.Count == 0 ? null : scores.Max(),
            ScoreDistribution = BuildScoreDistribution(scores),
            CompletedThisYear = completedDates.Count(date => date.Year == today.Year),
            CompletedLast30Days = completedDates.Count(date => date >= thirtyDaysAgo && date <= today),
            Spotlight = watching.FirstOrDefault(),
            ContinueWatching = watching.Skip(1).Take(6).ToList(),
            RecentlyCompleted = allEntries
                .Where(item => item.CatalogEntry.CompletedAt is not null)
                .OrderByDescending(item => item.CatalogEntry.CompletedAt)
                .Take(6)
                .ToList(),
            HighestRated = allEntries
                .Where(item => item.CatalogEntry.Score is not null)
                .OrderByDescending(item => item.CatalogEntry.Score)
                .ThenBy(item => item.PrimaryTitle)
                .Take(6)
                .ToList(),
            RecentlyAdded = allEntries
                .OrderByDescending(item => item.CatalogEntry.CreatedAt)
                .Take(6)
                .ToList(),
            TopFranchises = realFranchises
                .OrderByDescending(item => item.CompletedCount)
                .ThenByDescending(item => item.EntryCount)
                .ThenBy(item => item.Title)
                .Take(4)
                .ToList()
        };
    }

    /// <summary>All five statuses in enum order, including the ones nothing sits in.</summary>
    private static IReadOnlyList<StatusCount> BuildStatusBreakdown(IReadOnlyList<AnimeListItemViewModel> entries) =>
        Enum.GetValues<CatalogStatus>()
            .Select(status => new StatusCount(status, entries.Count(entry => entry.CatalogEntry.Status == status)))
            .ToList();

    /// <summary>
    /// Episode rollup. An entry with no known episode count contributes nothing to the total and
    /// instead flags the result, so callers can render "n+" rather than an understated denominator.
    /// </summary>
    private static (int Watched, int Total, bool HasUnknown) SumEpisodes(IReadOnlyList<AnimeListItemViewModel> entries) =>
        (entries.Sum(entry => entry.CatalogEntry.EpisodesWatched),
         entries.Sum(entry => entry.AnimeEntry.Episodes ?? 0),
         entries.Any(entry => entry.AnimeEntry.Episodes is null));

    /// <summary>
    /// Floors each score into a whole-number bucket and drops the empty ones, so a small catalog
    /// renders a few honest bars instead of ten mostly-blank ones. Scores below 1 fold into bucket 1.
    /// </summary>
    private static IReadOnlyList<ScoreBucket> BuildScoreDistribution(IReadOnlyList<decimal> scores) =>
        scores
            .GroupBy(score => Math.Clamp((int)Math.Floor(score), 1, 10))
            .Select(group => new ScoreBucket(group.Key, group.Count()))
            .OrderByDescending(bucket => bucket.Score)
            .ToList();

    public AdminDashboardViewModel BuildAdminSummary(
        IReadOnlyList<AnimeEntry> animeEntries,
        IReadOnlyList<CatalogEntry> catalogEntries,
        IReadOnlyList<AnimeRelation> relations,
        IReadOnlyList<Franchise> franchises,
        bool publicCatalogEnabled)
    {
        return new AdminDashboardViewModel
        {
            FranchiseCount = franchises.Count,
            AnimeEntryCount = animeEntries.Count,
            RelationsCount = relations.Count,
            CompletedCount = catalogEntries.Count(item => item.Status == CatalogStatus.Completed),
            WatchingCount = catalogEntries.Count(item => item.Status == CatalogStatus.Watching),
            PublicCatalogEnabled = publicCatalogEnabled
        };
    }

    private static bool MatchesFilters(AnimeListItemViewModel entry, string query, CatalogStatus? status)
    {
        if (status is not null && entry.CatalogEntry.Status != status.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return entry.AnimeEntry.TitleRomaji.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.AnimeEntry.TitleEnglish?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IReadOnlyList<FranchiseSummaryViewModel> ApplySort(IReadOnlyList<FranchiseSummaryViewModel> items, CatalogSortOption sort)
    {
        return sort switch
        {
            CatalogSortOption.ScoreDescending => items.OrderByDescending(item => item.AverageScore ?? -1).ThenBy(item => item.Title).ToList(),
            CatalogSortOption.RecentlyAdded => items.OrderByDescending(item => item.Entries.Max(entry => entry.CatalogEntry.CreatedAt)).ToList(),
            CatalogSortOption.RecentlyCompleted => items.OrderByDescending(item => item.Entries.Max(entry => entry.CatalogEntry.CompletedAt?.ToDateTime(TimeOnly.MinValue))).ToList(),
            CatalogSortOption.Year => items.OrderByDescending(item => item.Entries.Max(entry => entry.AnimeEntry.SeasonYear)).ThenBy(item => item.Title).ToList(),
            _ => items.OrderBy(item => item.Title).ToList()
        };
    }
}
