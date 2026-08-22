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

    // The calendar and archive cards read a fraction of EnrichFields, so they get their own, much
    // smaller fragment. EnrichFields carries description, tags, rankings, studios, synonyms and a
    // whole relations connection - roughly 8-18 KB per media, which is 400-900 KB per page of 50 and
    // 3-6 MB for a seven-page week. This set is about 500 bytes per media.
    //
    // Request cost is identical either way, since AniList counts requests rather than bytes. The
    // reasons are the payload on a phone, the cost of deserialising it all on the WASM heap for
    // fields no card renders, and - the one that actually matters - keeping the two shapes visibly
    // distinct. AniListMedia is one wide class where most fields are null depending on which query
    // filled it, so if calendar results looked like enrichment results someone would eventually
    // write them into the id-keyed enrichment cache, which would then serve half-populated media to
    // the details page and to the franchise-gap walk. That walk yields nothing when Relations.Edges
    // is empty, and it would fail silently. Calendar results are never cached as enrichment.
    private const string CalendarFields = """
        fragment CalendarFields on Media {
          id
          type
          title {
            romaji
            english
            native
          }
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
          genres
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
          nextAiringEpisode {
            episode
            airingAt
            timeUntilAiring
          }
        }
        """;

    // sort: [TIME] is load-bearing, not a preference. A week takes five to seven sequential pages,
    // so sorting by time makes every partial result a chronological prefix: the day columns fill
    // Monday to Sunday and "complete through Friday 18:00" is a truthful statement. Under any other
    // sort a half-loaded week is silently wrong everywhere at once.
    //
    // pageInfo asks for hasNextPage and nothing else. total and lastPage are known to lie on page 1
    // - see AniListPageInfo - so not selecting them means no caller can build a count on fiction.
    private const string AiringScheduleQuery = $$"""
        query ($page: Int!, $perPage: Int!, $airingAtGreater: Int!, $airingAtLesser: Int!) {
          Page(page: $page, perPage: $perPage) {
            pageInfo {
              currentPage
              hasNextPage
            }
            airingSchedules(
              airingAt_greater: $airingAtGreater
              airingAt_lesser: $airingAtLesser
              sort: [TIME]
            ) {
              id
              airingAt
              timeUntilAiring
              episode
              mediaId
              media {
                ...CalendarFields
              }
            }
          }
        }

        {{CalendarFields}}
        """;

    // Every filter is a nullable variable. JsonDefaults.Web drops null properties, so an unset
    // filter is absent from the variables object entirely and GraphQL then skips the argument
    // outright - which is what makes "the whole year" work without depending on AniList choosing to
    // treat an explicit null as absent, something the spec does not promise.
    private const string BrowseMediaQuery = $$"""
        query (
          $page: Int!
          $perPage: Int!
          $season: MediaSeason
          $seasonYear: Int
          $sort: [MediaSort]
          $formatIn: [MediaFormat]
          $genreIn: [String]
          $countryOfOrigin: CountryCode
          $isAdult: Boolean
          $averageScoreGreater: Int
          $search: String
        ) {
          Page(page: $page, perPage: $perPage) {
            pageInfo {
              currentPage
              hasNextPage
            }
            media(
              type: ANIME
              season: $season
              seasonYear: $seasonYear
              sort: $sort
              format_in: $formatIn
              genre_in: $genreIn
              countryOfOrigin: $countryOfOrigin
              isAdult: $isAdult
              averageScore_greater: $averageScoreGreater
              search: $search
            ) {
              ...CalendarFields
            }
          }
        }

        {{CalendarFields}}
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

    public async Task<AniListPageResult<AniListAiringSchedule>> GetAiringSchedulesAsync(
        DateTimeOffset windowStartInclusive,
        DateTimeOffset windowEndExclusive,
        int page,
        int perPage,
        CancellationToken cancellationToken = default)
    {
        // airingAt_greater is strictly greater, so the start goes out one second early or an episode
        // airing exactly at midnight on the first day of the week is dropped from it.
        var greater = ToInt32UnixSeconds(
            windowStartInclusive.ToUnixTimeSeconds() - 1,
            nameof(windowStartInclusive));

        var lesser = ToInt32UnixSeconds(
            windowEndExclusive.ToUnixTimeSeconds(),
            nameof(windowEndExclusive));

        var variables = new
        {
            page,
            perPage = Math.Clamp(perPage, 1, MaxBatchSize),
            airingAtGreater = greater,
            airingAtLesser = lesser
        };

        var response = await SendAsync(variables, AiringScheduleQuery, cancellationToken);
        var pageData = response.Data?.Page;

        return BuildPageResult(pageData?.AiringSchedules ?? [], pageData?.PageInfo, page);
    }

    public async Task<AniListPageResult<AniListMedia>> BrowseMediaAsync(
        AniListBrowseRequest request,
        int page,
        int perPage,
        CancellationToken cancellationToken = default)
    {
        // Anonymous property names must match the $variable names in BrowseMediaQuery exactly -
        // JsonSerializerDefaults.Web camel-cases them, so averageScoreGreater lands as
        // "averageScoreGreater". A rename here compiles fine and fails only at runtime, which is why
        // a contract test asserts the mapping.
        //
        // Nulls are dropped by JsonDefaults.Web, so an unset filter never reaches AniList as an
        // explicit null; GraphQL skips the argument instead.
        var variables = new
        {
            page,
            perPage = Math.Clamp(perPage, 1, MaxBatchSize),
            season = string.IsNullOrWhiteSpace(request.Season) ? null : request.Season,
            seasonYear = request.SeasonYear,
            sort = new[] { request.Sort },
            formatIn = request.Formats.Count == 0 ? null : request.Formats.ToArray(),
            genreIn = request.Genres.Count == 0 ? null : request.Genres.ToArray(),
            countryOfOrigin = string.IsNullOrWhiteSpace(request.CountryOfOrigin) ? null : request.CountryOfOrigin,
            isAdult = request.IsAdult,
            averageScoreGreater = request.MinimumAverageScore,
            search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim()
        };

        var response = await SendAsync(variables, BrowseMediaQuery, cancellationToken);
        var pageData = response.Data?.Page;

        return BuildPageResult(pageData?.Media ?? [], pageData?.PageInfo, page);
    }

    /// <summary>
    /// Reconciles AniList's paging flag with what actually arrived.
    /// </summary>
    /// <remarks>
    /// AniList reports hasNextPage true on the page past the last one, so the empty-page check is
    /// what stops a walk. Trusting the flag alone runs every browse to its page cap and spends five
    /// paced requests learning there was nothing more.
    /// </remarks>
    private static AniListPageResult<T> BuildPageResult<T>(
        IReadOnlyList<T> items,
        AniListPageInfo? pageInfo,
        int page) =>
        new(items, page, items.Count > 0 && (pageInfo?.HasNextPage ?? false));

    /// <summary>
    /// Narrows a unix timestamp to the 32-bit range AniList's Int filter arguments accept.
    /// </summary>
    /// <remarks>
    /// Checked rather than cast: unchecked, a post-2038 bound silently wraps to a negative timestamp
    /// and AniList answers with a completely different week, which is far worse than a clear
    /// argument error. Nothing is checked on the way in - airingAt is read as a long - so the
    /// asymmetry lives here and only here.
    /// </remarks>
    private static int ToInt32UnixSeconds(long unixSeconds, string parameterName)
    {
        if (unixSeconds is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                unixSeconds,
                "AniList's airingAt filters are 32-bit, so the window must fall before 2038-01-19.");
        }

        return (int)unixSeconds;
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

            HttpResponseMessage response;

            // In WebAssembly a CORS rejection, a dropped connection and an offline browser all
            // arrive as HttpRequestException("TypeError: Failed to fetch"), which is useless to
            // show and indistinguishable from a bug in this app. Classified here so every caller
            // sees the same "AniList is not answering" type it gets for a 403.
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new AniListUnavailableException(null, exception.Message, exception);
            }

            using (response)
            {
                var statusCode = (int)response.StatusCode;
                var hasAttemptsLeft = attempt < RetryBackoff.Length;

                // 429 is rate limiting and 5xx is AniList being briefly unwell; both are worth
                // another try. Anything else is a real answer, success or not, and falls through
                // immediately. 403 is deliberately NOT here: it is a sustained kill switch, so it
                // will still be one 1.6 seconds later, and retrying only makes the failure slower
                // while spending two more requests against a limit shared with the whole page.
                if (hasAttemptsLeft && (statusCode == 429 || statusCode >= 500))
                {
                    lastTransportStatus = statusCode;
                    await Task.Delay(ResolveRetryDelay(response, attempt), cancellationToken);
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // AniList sends its GraphQL error envelope on failures too, so its own words
                    // are in the body. EnsureSuccessStatusCode used to fire before this was read
                    // and threw all of it away.
                    var serverMessage = TryReadFirstErrorMessage(payload);

                    if (IsUnavailableStatus(statusCode))
                    {
                        throw new AniListUnavailableException(statusCode, serverMessage);
                    }

                    // A 4xx that is not one of the above means the document or the variables are
                    // wrong, which is our bug. It has to stay loud and specific.
                    throw new InvalidOperationException(
                        serverMessage ?? $"AniList returned HTTP {statusCode}.");
                }

                var graphQlResponse = JsonSerializer.Deserialize<AniListGraphQlResponse<AniListMediaData>>(payload, JsonDefaults.Web)
                    ?? throw new InvalidOperationException("AniList returned an empty payload.");

                if (graphQlResponse.Errors is { Count: > 0 })
                {
                    var error = graphQlResponse.Errors[0];

                    // The disabled-API notice also arrives as a 200 carrying the real status in the
                    // error body, so the same classification has to run on this path.
                    if (error.Status is { } bodyStatus && IsUnavailableStatus(bodyStatus))
                    {
                        throw new AniListUnavailableException(bodyStatus, error.Message);
                    }

                    throw new InvalidOperationException(error.Message);
                }

                return graphQlResponse;
            }
        }

        throw new AniListUnavailableException(
            lastTransportStatus == 0 ? null : lastTransportStatus,
            $"AniList request failed after {RetryBackoff.Length + 1} attempts (last status {lastTransportStatus}).");
    }

    /// <summary>
    /// Statuses that mean "AniList is not answering", as opposed to "AniList rejected this query".
    /// </summary>
    /// <remarks>
    /// 403 is the current outage kill switch, 429 is a rate limit that already exhausted its
    /// retries, 404 is the endpoint itself being pulled, and 5xx is the service being unwell. None
    /// of them is actionable by the visitor and none of them implicates the query, so they are all
    /// one condition as far as the UI is concerned.
    /// </remarks>
    private static bool IsUnavailableStatus(int statusCode) =>
        statusCode is 403 or 404 or 429 || statusCode >= 500;

    /// <summary>
    /// Pulls <c>errors[0].message</c> out of a body that may not be JSON at all - a proxy or a CDN
    /// sitting in front of AniList can answer with HTML, and that must not turn into a parse
    /// exception that hides the status code the caller actually needs.
    /// </summary>
    private static string? TryReadFirstErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AniListGraphQlResponse<AniListMediaData>>(payload, JsonDefaults.Web);
            var message = parsed?.Errors?.FirstOrDefault()?.Message;
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch (JsonException)
        {
            return null;
        }
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
