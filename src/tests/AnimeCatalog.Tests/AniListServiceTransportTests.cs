using System.Net;
using System.Text;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Options;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// Covers how <see cref="AniListService"/> classifies a failed request.
/// </summary>
/// <remarks>
/// The distinction under test is "AniList is not answering" versus "AniList rejected our query".
/// Before it existed, a 403 reached the pages as HttpRequestException("TypeError: Failed to fetch")
/// out of EnsureSuccessStatusCode, which fired before the body was read and threw away the only
/// useful text AniList had sent.
/// </remarks>
public sealed class AniListServiceTransportTests
{
    private const string DisabledApiBody = """
        {"errors":[{"message":"The AniList API has been temporarily disabled due to severe stability issues.","status":403}]}
        """;

    private const string EmptyPage = """{"data":{"Page":{"media":[]}}}""";

    [Fact]
    public async Task DisabledApi_IsReportedAsUnavailableWithAniListsOwnWords()
    {
        var handler = new StubHandler((HttpStatusCode)403, DisabledApiBody);
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Equal(403, exception.StatusCode);
        Assert.Contains("temporarily disabled", exception.ServerMessage);

        // The visitor-facing message has to be the actionable one, not AniList's internal note.
        Assert.Equal(AniListUnavailableException.DefaultMessage, exception.Message);
    }

    // Pins the deliberate omission of 403 from RetryBackoff: a kill switch will still be a kill
    // switch 1.6 seconds later, so retrying only slows the failure down while spending two more
    // requests against a limit shared with the whole page.
    [Fact]
    public async Task Forbidden_IsNotRetried()
    {
        var handler = new StubHandler((HttpStatusCode)403, DisabledApiBody);
        var service = Create(handler);

        await Assert.ThrowsAsync<AniListUnavailableException>(() => service.SearchAnimeAsync("frieren"));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RateLimit_IsRetriedAndCanSucceed()
    {
        var handler = new StubHandler([
            new StubResponse((HttpStatusCode)429, "{}"),
            new StubResponse(HttpStatusCode.OK, EmptyPage)
        ]);
        var service = Create(handler);

        var results = await service.SearchAnimeAsync("frieren");

        Assert.Empty(results);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ServerErrors_AreRetriedUntilTheAttemptsRunOut()
    {
        var handler = new StubHandler([new StubResponse(HttpStatusCode.BadGateway, "{}")]);
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ATransportFailure_IsReportedAsUnavailableRatherThanAsFailedToFetch()
    {
        var handler = new ThrowingHandler(new HttpRequestException("TypeError: Failed to fetch"));
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Equal(AniListUnavailableException.DefaultMessage, exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    // AniList sometimes answers 200 and carries the real status inside the error envelope instead.
    [Fact]
    public async Task DisabledApi_IsStillRecognisedWhenItArrivesOnASuccessfulStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, DisabledApiBody);
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task AQueryError_StaysALoudInvalidOperation()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"errors":[{"message":"Cannot query field on type Media."}]}
            """);
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Contains("Cannot query field", exception.Message);
    }

    // A 400 means our document or our variables are wrong. It must not be dressed up as an outage.
    [Fact]
    public async Task ABadRequest_SurfacesTheServerMessageAsAnInvalidOperation()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """
            {"errors":[{"message":"Variable of required type was not provided.","status":400}]}
            """);
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.IsNotType<AniListUnavailableException>(exception);
        Assert.Contains("was not provided", exception.Message);
    }

    // A CDN or proxy in front of AniList can answer with HTML. Failing to parse that must not hide
    // the status code, which is the part the caller actually needs.
    [Fact]
    public async Task ANonJsonErrorBody_DoesNotBecomeAParseFailure()
    {
        var handler = new StubHandler((HttpStatusCode)503, "<html><body>Service Unavailable</body></html>");
        var service = Create(handler);

        var exception = await Assert.ThrowsAsync<AniListUnavailableException>(
            () => service.SearchAnimeAsync("frieren"));

        Assert.Equal(503, exception.StatusCode);
        Assert.Null(exception.ServerMessage);
    }

    private static AniListService Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new AniListOptions { GraphQlUrl = "https://graphql.anilist.co" }));

    private sealed record StubResponse(HttpStatusCode StatusCode, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<StubResponse> _responses;

        public StubHandler(HttpStatusCode statusCode, string body)
            : this([new StubResponse(statusCode, body)])
        {
        }

        public StubHandler(List<StubResponse> responses) => _responses = responses;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The last entry repeats, so a single-response stub also answers every retry.
            var response = _responses[Math.Min(CallCount, _responses.Count - 1)];
            CallCount++;

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw _exception;
    }
}
