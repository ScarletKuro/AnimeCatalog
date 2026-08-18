using System.Net;
using System.Text;
using AnimeCatalog.Options;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// PostgREST silently truncates an unbounded response at its <c>max-rows</c> setting. That had left 616
/// of 1616 <c>anime_relations</c> rows invisible to the app and missing from catalog exports, so these
/// tests pin the paging behaviour that reads a table in full.
/// </summary>
public sealed class SupabaseRestPagingTests
{
    private const int PageSize = 1000;

    [Fact]
    public async Task SelectAsync_KeepsPagingUntilTheServerReturnsAShortPage()
    {
        // 1616 rows is the real table size that exposed the bug.
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        var rows = await service.SelectAsync<Row>("anime_relations");

        Assert.Equal(1616, rows.Count);
        Assert.Equal(2, handler.RequestCount);

        // Every row exactly once: no gaps, no duplicates across the page boundary.
        Assert.Equal(1616, rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(1, rows.Min(row => row.Id));
        Assert.Equal(1616, rows.Max(row => row.Id));
    }

    [Fact]
    public async Task SelectAsync_SecondPageAsksForTheNextWindow()
    {
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        await service.SelectAsync<Row>("anime_relations");

        Assert.Equal($"limit={PageSize}", QueryValue(handler.Uris[0], "limit"));
        Assert.Null(QueryValue(handler.Uris[0], "offset"));
        Assert.Equal($"offset={PageSize}", QueryValue(handler.Uris[1], "offset"));
    }

    [Fact]
    public async Task SelectAsync_OrdersByIdSoPagingIsStable()
    {
        var handler = new PagingHandler(totalRows: 5);
        var service = CreateService(handler);

        await service.SelectAsync<Row>("anime_relations");

        Assert.Equal("order=id.asc", QueryValue(handler.Uris[0], "order"));
    }

    [Fact]
    public async Task SelectAsync_AcceptsACallerSuppliedOrder()
    {
        var handler = new PagingHandler(totalRows: 5);
        var service = CreateService(handler);

        await service.SelectAsync<Row>("anime_relations", order: "created_at.desc");

        Assert.Equal("order=created_at.desc", QueryValue(handler.Uris[0], "order"));
    }

    [Fact]
    public async Task SelectAsync_MakesASingleRequestWhenEverythingFitsInOnePage()
    {
        var handler = new PagingHandler(totalRows: 42);
        var service = CreateService(handler);

        var rows = await service.SelectAsync<Row>("franchises");

        Assert.Equal(42, rows.Count);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SelectAsync_StopsAtExactlyOnePageWhenTheTableIsEmpty()
    {
        var handler = new PagingHandler(totalRows: 0);
        var service = CreateService(handler);

        Assert.Empty(await service.SelectAsync<Row>("franchises"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SelectAsync_MakesATrailingRequestWhenTheTotalIsAnExactMultipleOfThePage()
    {
        // A full final page is indistinguishable from "more to come", so one extra empty page is
        // expected rather than a wrong row count.
        var handler = new PagingHandler(totalRows: PageSize);
        var service = CreateService(handler);

        var rows = await service.SelectAsync<Row>("anime_relations");

        Assert.Equal(PageSize, rows.Count);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SelectAsync_HonoursACallerSuppliedLimitWithoutPaging()
    {
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        var rows = await service.SelectAsync<Row>("anime_relations", new Dictionary<string, string>
        {
            ["limit"] = "5"
        });

        Assert.Equal(5, rows.Count);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SelectAsync_HonoursACallerSuppliedOffsetWithoutPaging()
    {
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        await service.SelectAsync<Row>("anime_relations", new Dictionary<string, string>
        {
            ["offset"] = "10"
        });

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SelectAsync_KeepsCallerFiltersOnEveryPage()
    {
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        await service.SelectAsync<Row>("anime_relations", new Dictionary<string, string>
        {
            ["source_anime_id"] = "eq.480"
        });

        Assert.Equal(2, handler.RequestCount);
        Assert.All(handler.Uris, uri => Assert.Contains("source_anime_id=eq.480", uri.Query));
    }

    [Fact]
    public async Task SelectSingleAsync_AsksForOneRowInsteadOfAWholeTable()
    {
        var handler = new PagingHandler(totalRows: 1616);
        var service = CreateService(handler);

        var row = await service.SelectSingleAsync<Row>("anime_entries", new Dictionary<string, string>
        {
            ["anilist_id"] = "eq.143338"
        });

        Assert.NotNull(row);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("limit=1", QueryValue(handler.Uris[0], "limit"));
    }

    private static SupabaseRestService CreateService(PagingHandler handler) =>
        new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "sb_publishable_123"
            }),
            new StubTokenProvider());

    private static string? QueryValue(Uri uri, string key) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith(key + "=", StringComparison.Ordinal));

    private sealed record Row(int Id);

    /// <summary>Serves sequential ids and applies whatever limit/offset the request asks for.</summary>
    private sealed class PagingHandler : HttpMessageHandler
    {
        private readonly int _totalRows;

        public PagingHandler(int totalRows) => _totalRows = totalRows;

        public int RequestCount { get; private set; }

        public List<Uri> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Uris.Add(request.RequestUri!);

            var query = request.RequestUri!.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

            var offset = query.TryGetValue("offset", out var rawOffset) ? int.Parse(rawOffset) : 0;
            var limit = query.TryGetValue("limit", out var rawLimit) ? int.Parse(rawLimit) : _totalRows;

            var ids = Enumerable.Range(1, _totalRows).Skip(offset).Take(limit);
            var json = "[" + string.Join(",", ids.Select(id => $"{{\"Id\":{id}}}")) + "]";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("token-abc");
    }
}
