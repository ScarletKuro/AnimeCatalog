using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Services;

/// <summary>
/// Finds anime you have not catalogued that belong to franchises you have watched.
/// </summary>
/// <remarks>
/// <para>
/// AniList relations are one hop, so listing a watched anime's direct relations is not enough: Darker
/// than Black links season one to a special, which links to another special, which links to season
/// two. Anything shallower than a full walk of the connected component misses it. The walk therefore
/// runs breadth-first until the component is exhausted or a hard node budget stops it.
/// </para>
/// <para>
/// Seeds are fetched from AniList rather than read from <c>anime_relations</c>: those rows are a
/// snapshot from when each anime was added, so a sequel announced afterwards would be absent — exactly
/// the case this exists to catch.
/// </para>
/// </remarks>
public sealed class FranchiseGapService
{
    /// <summary>Ceiling on titles fetched in one scan, so a pathological graph cannot run away.</summary>
    public const int MaxNodes = 1500;

    /// <summary>Matches the AniList page size, so progress is reported per request.</summary>
    private const int BatchSize = 50;

    private static readonly CatalogStatus[] SeedStatuses = [CatalogStatus.Completed, CatalogStatus.Watching];

    private readonly IAniListEnrichmentService _enrichmentService;

    public FranchiseGapService(IAniListEnrichmentService enrichmentService)
    {
        _enrichmentService = enrichmentService;
    }

    public async Task<FranchiseGapScanViewModel> ScanAsync(
        RepositorySnapshot snapshot,
        IProgress<FranchiseGapScanViewModel>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Anything in the catalog is already tracked, whatever its status, so it can never be
        // "missing" — but only finished and in-progress entries are worth searching outward from.
        var catalogByAnimeId = snapshot.CatalogEntries
            .GroupBy(entry => entry.AnimeEntryId)
            .ToDictionary(group => group.Key, group => group.First());

        var catalogued = snapshot.AnimeEntries.Select(entry => entry.AniListId).ToHashSet();

        var seeds = snapshot.AnimeEntries
            .Where(entry => catalogByAnimeId.TryGetValue(entry.Id, out var catalogEntry)
                         && SeedStatuses.Contains(catalogEntry.Status))
            .Select(entry => entry.AniListId)
            .Where(id => id > 0)
            .ToHashSet();

        var fetched = new Dictionary<int, AniListMedia>();
        var discovery = new Dictionary<int, Discovery>();
        var frontier = seeds.ToList();
        var truncated = false;

        while (frontier.Count > 0 && !truncated)
        {
            var next = new List<int>();

            foreach (var batch in Chunk(frontier.Where(id => !fetched.ContainsKey(id)).Distinct().ToList()))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (fetched.Count >= MaxNodes)
                {
                    truncated = true;
                    break;
                }

                var media = await _enrichmentService.GetManyAsync(batch, cancellationToken);

                foreach (var (id, item) in media)
                {
                    fetched[id] = item;
                }

                // A batch is one request, so reporting here is what makes results stream in.
                progress?.Report(Build(fetched, discovery, catalogued, seeds, snapshot, truncated));

                next.AddRange(Expand(batch, fetched, discovery));
            }

            frontier = next;
        }

        return Build(fetched, discovery, catalogued, seeds, snapshot, truncated);
    }

    /// <summary>
    /// Queues the traversable neighbours of everything just fetched, recording how each was reached.
    /// </summary>
    private static IEnumerable<int> Expand(
        IReadOnlyList<int> batch,
        IReadOnlyDictionary<int, AniListMedia> fetched,
        Dictionary<int, Discovery> discovery)
    {
        foreach (var id in batch)
        {
            if (!fetched.TryGetValue(id, out var media) || !AnimeRelationRules.IsAnimeType(media.Type))
            {
                // Never expand through a manga: its relations lead away into another medium entirely.
                continue;
            }

            foreach (var edge in media.Relations?.Edges ?? [])
            {
                if (edge.Node is null || !AnimeRelationRules.IsTraversable(edge.RelationType))
                {
                    continue;
                }

                // First discovery wins, which keeps the shortest path's explanation.
                if (!discovery.ContainsKey(edge.Node.Id))
                {
                    discovery[edge.Node.Id] = new Discovery(
                        edge.RelationType ?? "OTHER",
                        media.Title.English ?? media.Title.Romaji);
                }

                if (!fetched.ContainsKey(edge.Node.Id))
                {
                    yield return edge.Node.Id;
                }
            }
        }
    }

    /// <summary>
    /// Groups everything fetched into connected franchises and keeps those containing a seed.
    /// </summary>
    private static FranchiseGapScanViewModel Build(
        IReadOnlyDictionary<int, AniListMedia> fetched,
        IReadOnlyDictionary<int, Discovery> discovery,
        IReadOnlySet<int> catalogued,
        IReadOnlySet<int> seeds,
        RepositorySnapshot snapshot,
        bool truncated)
    {
        var components = BuildComponents(fetched);

        var entriesByAniListId = snapshot.AnimeEntries
            .GroupBy(entry => entry.AniListId)
            .ToDictionary(group => group.Key, group => group.First());

        var franchisesById = snapshot.Franchises.ToDictionary(item => item.Id);

        var groups = new List<FranchiseGapGroupViewModel>();

        foreach (var component in components)
        {
            // Only franchises you have actually started are of interest.
            if (!component.Any(seeds.Contains))
            {
                continue;
            }

            var missing = component
                .Where(id => !catalogued.Contains(id))
                .Select(id => fetched[id])
                .Where(media => AnimeRelationRules.IsAnimeType(media.Type) && !AnimeRelationRules.IsMusicFormat(media.Format))
                .Select(media => new MissingAnimeViewModel
                {
                    Media = media,
                    RelationType = discovery.GetValueOrDefault(media.Id)?.RelationType ?? "OTHER",
                    DiscoveredFrom = discovery.GetValueOrDefault(media.Id)?.FromTitle
                })
                // Unrated titles are usually unaired, so they sort last rather than as a zero.
                .OrderByDescending(item => item.Score ?? -1)
                .ThenBy(item => item.Title)
                .ToList();

            if (missing.Count == 0)
            {
                continue;
            }

            var ownedIds = component.Where(catalogued.Contains).ToList();
            var (title, slug) = NameGroup(ownedIds, fetched, entriesByAniListId, franchisesById);

            groups.Add(new FranchiseGapGroupViewModel
            {
                Title = title,
                FranchiseSlug = slug,
                OwnedCount = ownedIds.Count,
                TotalCount = component.Count,
                Missing = missing
            });
        }

        return new FranchiseGapScanViewModel
        {
            Groups = groups
                .OrderByDescending(group => group.BestScore ?? -1)
                .ThenBy(group => group.Title)
                .ToList(),
            ScannedCount = fetched.Count,
            WasTruncated = truncated
        };
    }

    /// <summary>
    /// Names a franchise from what you own in it: the local grouping when there is one, otherwise the
    /// most popular title you watched. Local grouping is therefore optional, never required.
    /// </summary>
    private static (string Title, string? Slug) NameGroup(
        IReadOnlyList<int> ownedIds,
        IReadOnlyDictionary<int, AniListMedia> fetched,
        IReadOnlyDictionary<int, AnimeEntry> entriesByAniListId,
        IReadOnlyDictionary<long, Franchise> franchisesById)
    {
        var ownedEntries = ownedIds
            .Select(entriesByAniListId.GetValueOrDefault)
            .OfType<AnimeEntry>()
            .ToList();

        var franchiseIds = ownedEntries
            .Where(entry => entry.FranchiseId is not null)
            .Select(entry => entry.FranchiseId!.Value)
            .Distinct()
            .ToList();

        if (franchiseIds.Count == 1 && franchisesById.TryGetValue(franchiseIds[0], out var franchise))
        {
            return (franchise.Title, franchise.Slug);
        }

        var flagship = ownedIds
            .Select(fetched.GetValueOrDefault)
            .OfType<AniListMedia>()
            .OrderByDescending(media => media.Popularity ?? 0)
            .FirstOrDefault();

        var fallback = flagship?.Title.English
            ?? flagship?.Title.Romaji
            ?? ownedEntries.FirstOrDefault()?.TitleEnglish
            ?? ownedEntries.FirstOrDefault()?.TitleRomaji;

        return (string.IsNullOrWhiteSpace(fallback) ? "Unnamed franchise" : fallback, null);
    }

    /// <summary>Union-find over the traversable edges between fetched anime nodes.</summary>
    private static List<List<int>> BuildComponents(IReadOnlyDictionary<int, AniListMedia> fetched)
    {
        var parent = fetched.Keys.ToDictionary(id => id, id => id);

        int Find(int id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }

            return id;
        }

        void Union(int left, int right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a != b)
            {
                parent[b] = a;
            }
        }

        foreach (var (id, media) in fetched)
        {
            if (!AnimeRelationRules.IsAnimeType(media.Type))
            {
                continue;
            }

            foreach (var edge in media.Relations?.Edges ?? [])
            {
                if (edge.Node is not null
                    && AnimeRelationRules.IsTraversable(edge.RelationType)
                    && fetched.ContainsKey(edge.Node.Id))
                {
                    Union(id, edge.Node.Id);
                }
            }
        }

        return fetched.Keys
            .GroupBy(Find)
            .Select(group => group.ToList())
            .ToList();
    }

    private static IEnumerable<List<int>> Chunk(List<int> ids)
    {
        for (var index = 0; index < ids.Count; index += BatchSize)
        {
            yield return ids.GetRange(index, Math.Min(BatchSize, ids.Count - index));
        }
    }

    private sealed record Discovery(string RelationType, string? FromTitle);
}
