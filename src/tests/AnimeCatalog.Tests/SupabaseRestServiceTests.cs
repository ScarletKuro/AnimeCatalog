using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Models.Supabase;
using AnimeCatalog.Options;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

public sealed class SupabaseRestServiceTests
{
    [Fact]
    public async Task SelectAsync_AddsApiKeyAndBearerToken()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(handler);
        var service = new SupabaseRestService(
            client,
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", PublishableKey = "sb_publishable_123" }),
            new StubTokenProvider("token-abc"));

        await service.SelectAsync<Dictionary<string, object>>("franchises");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("sb_publishable_123", handler.LastRequest!.Headers.GetValues("apikey").Single());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "token-abc"), handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SelectAsync_ThrowsPostgrestException_WhenApiReturnsError()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"message":"forbidden","code":"42501"}""", Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(handler);
        var service = new SupabaseRestService(
            client,
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", PublishableKey = "sb_publishable_123" }),
            new StubTokenProvider(null));

        var exception = await Assert.ThrowsAsync<PostgrestException>(() => service.SelectAsync<Dictionary<string, object>>("franchises"));
        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("42501", exception.Error.Code);
    }

    [Fact]
    public async Task InsertSingleAsync_DeserializesTypedRow()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":9,"anilist_id":175124,"title_romaji":"Nyaight of the Living Cat","display_order":0,"created_at":"2026-08-16T16:52:52.804633+00:00","updated_at":"2026-08-16T16:52:52.804633+00:00"}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var client = new HttpClient(handler);
        var service = new SupabaseRestService(
            client,
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions { Url = "https://example.supabase.co", PublishableKey = "sb_publishable_123" }),
            new StubTokenProvider("token-abc"));

        var row = await service.InsertSingleAsync<AnimeEntryRow>("anime_entries", new
        {
            anilist_id = 175124,
            title_romaji = "Nyaight of the Living Cat"
        });

        Assert.NotNull(row);
        Assert.Equal(9, row!.Id);
        Assert.Equal(175124, row.AniListId);
        Assert.Equal("Nyaight of the Living Cat", row.TitleRomaji);
    }

    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        private readonly string? _token;

        public StubTokenProvider(string? token)
        {
            _token = token;
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_token);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestedUris.Add(request.RequestUri!);
            return Task.FromResult(_handler(request));
        }
    }
}
