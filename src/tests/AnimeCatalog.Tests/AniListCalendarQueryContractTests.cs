using System.Text.Json;
using AnimeCatalog.Models.AniList;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// Holds the calendar and archive documents to the same standard as the enrichment query.
/// </summary>
/// <remarks>
/// Same reasoning as AniListQueryContractTests: a field the code reads but the document never
/// requests deserializes as null, and no unit test with hand-built fixtures can catch it because
/// those set the property directly. The selection sets are parsed by brace depth via
/// <see cref="GraphQlDocument"/> rather than searched as strings.
/// </remarks>
public sealed class AniListCalendarQueryContractTests
{
    private const string CalendarFragmentHeader = "fragment CalendarFields on Media";

    private static readonly DateTimeOffset WindowStart = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("id")]
    [InlineData("type")]              // gates the anime-vs-manga check the way EnrichFields does
    [InlineData("title")]
    [InlineData("coverImage")]
    [InlineData("format")]            // separates a TV_SHORT filler from a real entry
    [InlineData("status")]
    [InlineData("season")]
    [InlineData("seasonYear")]
    [InlineData("episodes")]          // the denominator in "7 / 12"
    [InlineData("duration")]
    [InlineData("genres")]            // the archive's genre filter
    [InlineData("averageScore")]      // the archive's score sort and the "AniList 82" footer
    [InlineData("meanScore")]
    [InlineData("popularity")]
    [InlineData("favourites")]
    [InlineData("countryOfOrigin")]   // the archive's country filter
    [InlineData("isAdult")]           // the adult filter
    [InlineData("siteUrl")]           // uncataloged cards link out to this
    [InlineData("startDate")]
    [InlineData("endDate")]
    [InlineData("nextAiringEpisode")]
    public async Task CalendarFields_AsksForEveryFieldACardReads(string field)
    {
        var fields = await CalendarFragmentFieldsAsync();

        Assert.Contains(field, fields);
    }

    // The test that stops someone "simplifying" by pointing the calendar at EnrichFields. That would
    // take a seven-page week from roughly 200 KB to several megabytes, and would let calendar results
    // pass for enrichment results in the id-keyed cache.
    [Theory]
    [InlineData("description")]
    [InlineData("tags")]
    [InlineData("rankings")]
    [InlineData("relations")]
    [InlineData("studios")]
    [InlineData("synonyms")]
    [InlineData("bannerImage")]
    public async Task CalendarFields_LeavesOutTheExpensiveEnrichmentFields(string field)
    {
        var fields = await CalendarFragmentFieldsAsync();

        Assert.DoesNotContain(field, fields);
    }

    [Fact]
    public async Task CalendarFields_RequestsTheVersionedStatusEnum()
    {
        var captured = await CaptureAiringAsync();

        // v1 status still returns NOT_YET_AIRED, which is no longer a MediaStatus member.
        Assert.Contains("status(version: 2)", captured.Document);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("airingAt")]
    [InlineData("timeUntilAiring")]
    [InlineData("episode")]   // a missing one would deserialize as 0 and mislabel every row
    [InlineData("mediaId")]
    [InlineData("media")]
    public async Task AiringSchedulesQuery_SelectsEveryScheduleFieldTheAppReads(string field)
    {
        var captured = await CaptureAiringAsync();

        var page = GraphQlDocument.SelectionOf(
            captured.Document,
            GraphQlDocument.OperationSelectionStart(captured.Document),
            "Page");

        var schedules = GraphQlDocument.SelectionOf(captured.Document, page, "airingSchedules");

        Assert.Contains(field, GraphQlDocument.ImmediateFields(captured.Document, schedules));
    }

    // Without hasNextPage the paging walk cannot stop early, so every week would cost the full page
    // cap and the wall-clock that goes with it.
    [Fact]
    public async Task AiringSchedulesQuery_SelectsHasNextPage()
    {
        var captured = await CaptureAiringAsync();

        var page = GraphQlDocument.SelectionOf(
            captured.Document,
            GraphQlDocument.OperationSelectionStart(captured.Document),
            "Page");

        var pageInfo = GraphQlDocument.SelectionOf(captured.Document, page, "pageInfo");
        var fields = GraphQlDocument.ImmediateFields(captured.Document, pageInfo);

        Assert.Contains("hasNextPage", fields);

        // total and lastPage are deliberately absent - both were observed lying on page 1, so not
        // selecting them is what stops a count or a progress bar being built on them.
        Assert.DoesNotContain("total", fields);
        Assert.DoesNotContain("lastPage", fields);
    }

    // TIME sort is what makes a partial multi-page week a truthful chronological prefix.
    [Fact]
    public async Task AiringSchedulesQuery_SortsByTime()
    {
        var captured = await CaptureAiringAsync();

        Assert.Contains("sort: [TIME]", captured.Document);
    }

    // Guards against someone hardcoding page: 1 the way the four older documents do.
    [Fact]
    public async Task AiringSchedulesQuery_PassesPagingAsVariables()
    {
        var captured = await CaptureAiringAsync(page: 3, perPage: 50);

        Assert.Contains("$page: Int!", captured.Document);
        Assert.Contains("$perPage: Int!", captured.Document);
        Assert.Equal(3, captured.Variable("page").GetInt32());
        Assert.Equal(50, captured.Variable("perPage").GetInt32());
    }

    // airingAt_greater is strictly greater, so an episode airing exactly on the boundary is only
    // included when the argument is one second below the window start.
    [Fact]
    public async Task AiringSchedulesQuery_UsesAnInclusiveStartAndAnExclusiveEnd()
    {
        var captured = await CaptureAiringAsync();

        Assert.Equal(WindowStart.ToUnixTimeSeconds() - 1, captured.Variable("airingAtGreater").GetInt64());
        Assert.Equal(WindowEnd.ToUnixTimeSeconds(), captured.Variable("airingAtLesser").GetInt64());
    }

    [Fact]
    public async Task AiringSchedulesQuery_RefusesAWindowPastTheThirtyTwoBitLimit()
    {
        var handlerWasCalled = false;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await AniListRequestCapture.CaptureAsync(service =>
            {
                handlerWasCalled = true;
                return service.GetAiringSchedulesAsync(
                    new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2040, 1, 8, 0, 0, 0, TimeSpan.Zero),
                    1,
                    50);
            }));

        // The guard has to fire before a request is spent learning AniList disagrees.
        Assert.True(handlerWasCalled, "the call itself should have been attempted");
    }

    // The whole "an unset filter is an absent variable" design rests on this. Sending "season": null
    // would depend on AniList treating an explicit null as absent, which the spec does not promise.
    [Fact]
    public async Task BrowseQuery_OmitsTheSeasonVariableEntirelyForAWholeYearBrowse()
    {
        var captured = await CaptureBrowseAsync(new AniListBrowseRequest { SeasonYear = 2011 });

        Assert.False(captured.HasVariable("season"), "an unset season must not be sent at all");
        Assert.Equal(2011, captured.Variable("seasonYear").GetInt32());
    }

    [Fact]
    public async Task BrowseQuery_OmitsEveryUnsetFilter()
    {
        var captured = await CaptureBrowseAsync(new AniListBrowseRequest { SeasonYear = 2011, Season = "SPRING" });

        Assert.False(captured.HasVariable("formatIn"));
        Assert.False(captured.HasVariable("genreIn"));
        Assert.False(captured.HasVariable("countryOfOrigin"));
        Assert.False(captured.HasVariable("averageScoreGreater"));
        Assert.False(captured.HasVariable("search"));
    }

    [Fact]
    public async Task BrowseQuery_MapsEveryFilterOntoTheVariableNamesTheDocumentDeclares()
    {
        var captured = await CaptureBrowseAsync(new AniListBrowseRequest
        {
            SeasonYear = 2011,
            Season = "SPRING",
            Sort = "SCORE_DESC",
            Formats = ["TV", "MOVIE"],
            Genres = ["Action"],
            CountryOfOrigin = "JP",
            IsAdult = false,
            MinimumAverageScore = 70,
            Search = "  gundam  "
        });

        Assert.Equal("SPRING", captured.Variable("season").GetString());
        Assert.Equal(2011, captured.Variable("seasonYear").GetInt32());
        Assert.Equal("JP", captured.Variable("countryOfOrigin").GetString());
        Assert.False(captured.Variable("isAdult").GetBoolean());
        Assert.Equal(70, captured.Variable("averageScoreGreater").GetInt32());
        Assert.Equal("gundam", captured.Variable("search").GetString());

        // sort is a list even for one value, matching the [MediaSort] the document declares.
        Assert.Equal(
            ["SCORE_DESC"],
            captured.Variable("sort").EnumerateArray().Select(value => value.GetString()!).ToArray());

        Assert.Equal(
            ["TV", "MOVIE"],
            captured.Variable("formatIn").EnumerateArray().Select(value => value.GetString()!).ToArray());

        Assert.Equal(
            ["Action"],
            captured.Variable("genreIn").EnumerateArray().Select(value => value.GetString()!).ToArray());
    }

    // AniList enum values must travel as plain strings. JsonDefaults.Web registers a camelCase
    // JsonStringEnumConverter, so a C# enum would arrive as "scoreDesc" and be rejected.
    [Fact]
    public async Task BrowseQuery_SendsAniListEnumValuesInTheirOwnCasing()
    {
        var captured = await CaptureBrowseAsync(new AniListBrowseRequest
        {
            Season = "WINTER",
            Sort = "POPULARITY_DESC",
            Formats = ["TV_SHORT"]
        });

        Assert.Equal("WINTER", captured.Variable("season").GetString());
        Assert.Equal("POPULARITY_DESC", captured.Variable("sort")[0].GetString());
        Assert.Equal("TV_SHORT", captured.Variable("formatIn")[0].GetString());
    }

    [Fact]
    public async Task BrowseQuery_AsksAniListForAnimeOnly()
    {
        var captured = await CaptureBrowseAsync(new AniListBrowseRequest { SeasonYear = 2011 });

        Assert.Contains("type: ANIME", captured.Document);
    }

    // The empty-page fixture leaves AiringSchedules empty and PageInfo null. That has to come back as
    // an empty result rather than a null reference.
    [Fact]
    public async Task AnEmptyPage_ComesBackAsAnEmptyResultRatherThanThrowing()
    {
        AniListPageResult<AniListAiringSchedule>? result = null;

        await AniListRequestCapture.CaptureAsync(async service =>
            result = await service.GetAiringSchedulesAsync(WindowStart, WindowEnd, 1, 50));

        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.False(result.HasNextPage);
    }

    private static async Task<IReadOnlyCollection<string>> CalendarFragmentFieldsAsync()
    {
        var captured = await CaptureAiringAsync();

        return GraphQlDocument.ImmediateFields(
            captured.Document,
            GraphQlDocument.FragmentSelectionStart(captured.Document, CalendarFragmentHeader));
    }

    private static Task<AniListRequestCapture.CapturedRequest> CaptureAiringAsync(int page = 1, int perPage = 50) =>
        AniListRequestCapture.CaptureAsync(service =>
            service.GetAiringSchedulesAsync(WindowStart, WindowEnd, page, perPage));

    private static Task<AniListRequestCapture.CapturedRequest> CaptureBrowseAsync(AniListBrowseRequest request) =>
        AniListRequestCapture.CaptureAsync(service => service.BrowseMediaAsync(request, 1, 50));
}
