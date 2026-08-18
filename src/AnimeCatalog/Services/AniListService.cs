using System.Text;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Options;
using Microsoft.Extensions.Options;

namespace AnimeCatalog.Services;

public sealed class AniListService : IAniListService
{
    /// <summary>AniList caps <c>Page.perPage</c> at 50.</summary>
    public const int MaxBatchSize = 50;

    private const string SearchQuery = """
        query ($search: String!) {
          Page(page: 1, perPage: 10) {
            media(search: $search, type: ANIME) {
              id
              title {
                romaji
                english
                native
              }
              coverImage {
                large
                extraLarge
              }
              format
              season
              seasonYear
              episodes
              startDate {
                year
                month
                day
              }
              endDate {
                year
                month
                day
              }
              relations {
                edges {
                  relationType
                  node {
                    id
                    title {
                      romaji
                      english
                      native
                    }
                    format
                    coverImage {
                      large
                      extraLarge
                    }
                  }
                }
              }
            }
          }
        }
        """;

    private const string DetailsQuery = """
        query ($id: Int!) {
          Media(id: $id, type: ANIME) {
            id
            title {
              romaji
              english
              native
            }
            coverImage {
              large
              extraLarge
            }
            format
            season
            seasonYear
            episodes
            startDate {
              year
              month
              day
            }
            endDate {
              year
              month
              day
            }
            relations {
              edges {
                relationType
                node {
                  id
                  title {
                    romaji
                    english
                    native
                  }
                  format
                  coverImage {
                    large
                    extraLarge
                  }
                }
              }
            }
          }
        }
        """;

    // Requested only by the enrichment queries below. Kept as one fragment so the single-media and
    // batched documents can never drift apart.
    private const string EnrichmentFields = """
        fragment EnrichFields on Media {
          id
          type
          title {
            romaji
            english
            native
          }
          description(asHtml: false)
          bannerImage
          coverImage {
            extraLarge
            large
            color
          }
          format
          status(version: 2)
          season
          seasonYear
          episodes
          duration
          source(version: 3)
          genres
          synonyms
          averageScore
          meanScore
          popularity
          favourites
          countryOfOrigin
          isAdult
          siteUrl
          startDate {
            year
            month
            day
          }
          endDate {
            year
            month
            day
          }
          studios {
            edges {
              isMain
              node {
                id
                name
                siteUrl
              }
            }
          }
          tags {
            id
            name
            rank
            isMediaSpoiler
            isGeneralSpoiler
            category
          }
          rankings {
            rank
            type
            format
            year
            season
            allTime
            context
          }
          nextAiringEpisode {
            episode
            airingAt
            timeUntilAiring
          }
          relations {
            edges {
              relationType(version: 2)
              node {
                id
                type
                title {
                  romaji
                  english
                }
                format
                status(version: 2)
                seasonYear
                siteUrl
                coverImage {
                  extraLarge
                  large
                }
              }
            }
          }
        }
        """;

    private const string EnrichmentQuery = $$"""
        query ($id: Int!) {
          Media(id: $id, type: ANIME) {
            ...EnrichFields
          }
        }

        {{EnrichmentFields}}
        """;

    // AniList caps Page.perPage at 50, so callers chunk their id list before calling this.
    private const string EnrichmentBatchQuery = $$"""
        query ($ids: [Int]) {
          Page(page: 1, perPage: 50) {
            media(id_in: $ids, type: ANIME) {
              ...EnrichFields
            }
          }
        }

        {{EnrichmentFields}}
        """;

    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(1200)
    ];

    // Long enough to sit out a full rate-limit window. Clamping below it meant a 429 whose reset was
    // 45 seconds away fell back to a sub-second retry, burned both attempts and failed anyway.
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(70);

    private readonly HttpClient _httpClient;
    private readonly AniListOptions _options;

    public AniListService(HttpClient httpClient, IOptions<AniListOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<AniListMedia>> SearchAnimeAsync(string search, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return [];
        }

        var response = await SendAsync(new { search = search.Trim() }, SearchQuery, cancellationToken);
        return response.Data?.Page?.Media ?? [];
    }

    public async Task<AniListMedia?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new { id }, DetailsQuery, cancellationToken);
        return response.Data?.Media;
    }

    public async Task<AniListMedia?> GetEnrichedAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new { id }, EnrichmentQuery, cancellationToken);
        return response.Data?.Media;
    }

    public async Task<IReadOnlyList<AniListMedia>> GetEnrichedAnimeByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > MaxBatchSize)
        {
            throw new ArgumentException($"AniList allows at most {MaxBatchSize} ids per page; chunk the list first.", nameof(ids));
        }

        var response = await SendAsync(new { ids = ids.ToArray() }, EnrichmentBatchQuery, cancellationToken);
        return response.Data?.Page?.Media ?? [];
    }

    private async Task<AniListGraphQlResponse<AniListMediaData>> SendAsync(object variables, string query, CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.Serialize(new { query, variables }, JsonDefaults.Web);
        var lastTransportStatus = 0;

        for (var attempt = 0; attempt <= RetryBackoff.Length; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQlUrl)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;
            var hasAttemptsLeft = attempt < RetryBackoff.Length;

            // 429 is rate limiting and 5xx is AniList being briefly unwell; both are worth another
            // try. Anything else is a real answer, success or not, and falls through immediately.
            if (hasAttemptsLeft && (statusCode == 429 || statusCode >= 500))
            {
                lastTransportStatus = statusCode;
                await Task.Delay(ResolveRetryDelay(response, attempt), cancellationToken);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var graphQlResponse = JsonSerializer.Deserialize<AniListGraphQlResponse<AniListMediaData>>(payload, JsonDefaults.Web)
                ?? throw new InvalidOperationException("AniList returned an empty payload.");

            if (graphQlResponse.Errors is { Count: > 0 })
            {
                throw new InvalidOperationException(graphQlResponse.Errors[0].Message);
            }

            return graphQlResponse;
        }

        throw new InvalidOperationException($"AniList request failed after {RetryBackoff.Length + 1} attempts (last status {lastTransportStatus}).");
    }

    // AniList's CORS policy exposes only X-RateLimit-Limit/Remaining/Reset, so in a browser
    // response.Headers.RetryAfter is always null no matter what the server sent. X-RateLimit-Reset
    // is a unix timestamp for when the window reopens and is the only usable signal here.
    private TimeSpan ResolveRetryDelay(HttpResponseMessage response, int attempt)
    {
        var fallback = RetryBackoff[attempt];

        if (!response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
        {
            return fallback;
        }

        var raw = values.FirstOrDefault();
        if (!long.TryParse(raw, out var resetAtUnixSeconds))
        {
            return fallback;
        }

        var wait = DateTimeOffset.FromUnixTimeSeconds(resetAtUnixSeconds) - DateTimeOffset.UtcNow;

        // Clamped so a clock skew or a stale header cannot stall the page for minutes.
        return wait <= TimeSpan.Zero || wait > MaxRetryDelay ? fallback : wait;
    }
}
