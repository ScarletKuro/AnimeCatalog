using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Options;
using Microsoft.Extensions.Options;

namespace AnimeCatalog.Services;

public sealed class SupabaseRestService : ISupabaseRestService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _supabaseOptions;
    private readonly IAccessTokenProvider _accessTokenProvider;

    public SupabaseRestService(
        HttpClient httpClient,
        IOptions<SupabaseOptions> supabaseOptions,
        IAccessTokenProvider accessTokenProvider)
    {
        _httpClient = httpClient;
        _supabaseOptions = supabaseOptions.Value;
        _accessTokenProvider = accessTokenProvider;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_supabaseOptions.Url) &&
        !string.IsNullOrWhiteSpace(_supabaseOptions.PublishableKey);

    /// <summary>PostgREST caps an unbounded response; requests must page to read a whole table.</summary>
    private const int PageSize = 1000;

    /// <summary>Backstop so a server that ignores our paging cannot spin forever.</summary>
    private const int MaxPages = 200;

    /// <summary>
    /// Reads every matching row, paging until the server runs out.
    /// </summary>
    /// <remarks>
    /// A single unbounded GET silently stops at PostgREST's <c>max-rows</c> (1000 here), and the rows
    /// past it simply vanish — which had already made 616 of 1616 <c>anime_relations</c> invisible to
    /// the app and to catalog exports. Paging lives here rather than in each caller so no future query
    /// can reintroduce the truncation.
    /// </remarks>
    public async Task<List<T>> SelectAsync<T>(
        string table,
        IReadOnlyDictionary<string, string>? query = null,
        string select = "*",
        CancellationToken cancellationToken = default,
        string? order = "id.asc")
    {
        // A caller that bounded the query itself gets exactly what it asked for.
        if (HasExplicitWindow(query))
        {
            return await SendAsync<List<T>>(HttpMethod.Get, $"rest/v1/{table}", query, body: null, select, cancellationToken);
        }

        var results = new List<T>();

        for (var page = 0; page < MaxPages; page++)
        {
            // Paging without a deterministic sort can repeat and skip rows, so an order is always sent.
            var pageQuery = WithWindow(query, order, offset: page * PageSize, limit: PageSize);
            var batch = await SendAsync<List<T>>(HttpMethod.Get, $"rest/v1/{table}", pageQuery, body: null, select, cancellationToken);

            results.AddRange(batch);

            if (batch.Count < PageSize)
            {
                return results;
            }
        }

        throw new InvalidOperationException(
            $"Reading '{table}' exceeded {MaxPages} pages of {PageSize} rows; the server is not honouring the requested window.");
    }

    public async Task<T?> SelectSingleAsync<T>(
        string table,
        IReadOnlyDictionary<string, string> query,
        string select = "*",
        CancellationToken cancellationToken = default)
    {
        // Only the first row is ever used, so never ask the server for a whole table.
        var bounded = HasExplicitWindow(query) ? query : WithWindow(query, order: null, offset: 0, limit: 1);
        var items = await SendAsync<List<T>>(HttpMethod.Get, $"rest/v1/{table}", bounded, body: null, select, cancellationToken);
        return items.FirstOrDefault();
    }

    private static bool HasExplicitWindow(IReadOnlyDictionary<string, string>? query) =>
        query is not null && (query.ContainsKey("limit") || query.ContainsKey("offset"));

    private static Dictionary<string, string> WithWindow(
        IReadOnlyDictionary<string, string>? query,
        string? order,
        int offset,
        int limit)
    {
        var merged = query is null
            ? []
            : new Dictionary<string, string>(query, StringComparer.Ordinal);

        // A caller-supplied order wins: it may be ordering by something other than id.
        if (!string.IsNullOrWhiteSpace(order) && !merged.ContainsKey("order"))
        {
            merged["order"] = order;
        }

        merged["limit"] = limit.ToString();

        if (offset > 0)
        {
            merged["offset"] = offset.ToString();
        }

        return merged;
    }

    public Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default)
        => SendAsync<T?>(HttpMethod.Post, $"rest/v1/{table}", query: null, payload, select: "*", cancellationToken, prefer: "return=representation", accept: "application/vnd.pgrst.object+json");

    public Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default)
        => SendAsync<List<T>>(HttpMethod.Post, $"rest/v1/{table}", query: null, payload, select: "*", cancellationToken, prefer: "return=representation");

    public Task<T?> UpsertSingleAsync<T>(
        string table,
        object payload,
        string onConflictColumn,
        CancellationToken cancellationToken = default)
        => SendAsync<T?>(
            HttpMethod.Post,
            $"rest/v1/{table}",
            new Dictionary<string, string>
            {
                ["on_conflict"] = onConflictColumn
            },
            payload,
            select: "*",
            cancellationToken,
            prefer: "return=representation,resolution=merge-duplicates",
            accept: "application/vnd.pgrst.object+json");

    public Task<T?> UpdateSingleAsync<T>(
        string table,
        IReadOnlyDictionary<string, string> query,
        object payload,
        CancellationToken cancellationToken = default)
        => SendAsync<T?>(HttpMethod.Patch, $"rest/v1/{table}", query, payload, select: "*", cancellationToken, prefer: "return=representation", accept: "application/vnd.pgrst.object+json");

    public Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default)
        => SendAsync<object?>(HttpMethod.Delete, $"rest/v1/{table}", query, body: null, select: null, cancellationToken, prefer: "return=minimal");

    public Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default)
        => SendAsync<T?>(HttpMethod.Post, $"rest/v1/rpc/{functionName}", query: null, payload ?? new { }, select: null, cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string>? query,
        object? body,
        string? select,
        CancellationToken cancellationToken,
        string? prefer = null,
        string? accept = null)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Supabase configuration is incomplete.");
        }

        var uri = BuildUri(relativePath, query, select);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("apikey", _supabaseOptions.PublishableKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept ?? "application/json"));

        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrWhiteSpace(prefer))
        {
            request.Headers.Add("Prefer", prefer);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Web), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default!;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<PostgrestError>(payload, JsonDefaults.Web)
                ?? new PostgrestError { Message = payload };
            throw new PostgrestException(error, (int)response.StatusCode);
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)payload;
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, JsonDefaults.Web)
            ?? throw new InvalidOperationException($"Failed to deserialize Supabase payload for {relativePath}.");
    }

    private string BuildUri(string relativePath, IReadOnlyDictionary<string, string>? query, string? select)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(select))
        {
            parameters.Add($"select={Uri.EscapeDataString(select)}");
        }

        if (query is not null)
        {
            parameters.AddRange(query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        }

        var queryString = parameters.Count == 0 ? string.Empty : $"?{string.Join("&", parameters)}";
        return $"{_supabaseOptions.Url.TrimEnd('/')}/{relativePath}{queryString}";
    }
}
