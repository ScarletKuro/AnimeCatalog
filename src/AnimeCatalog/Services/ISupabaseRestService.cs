namespace AnimeCatalog.Services;

public interface ISupabaseRestService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Reads every matching row, paging past PostgREST's <c>max-rows</c> cap. Pass <paramref name="order"/>
    /// to page by a column other than <c>id</c>; a caller-supplied <c>limit</c> or <c>offset</c> in
    /// <paramref name="query"/> disables paging and is honoured verbatim.
    /// </summary>
    Task<List<T>> SelectAsync<T>(
        string table,
        IReadOnlyDictionary<string, string>? query = null,
        string select = "*",
        CancellationToken cancellationToken = default,
        string? order = "id.asc");

    Task<T?> SelectSingleAsync<T>(
        string table,
        IReadOnlyDictionary<string, string> query,
        string select = "*",
        CancellationToken cancellationToken = default);

    Task<T?> InsertSingleAsync<T>(string table, object payload, CancellationToken cancellationToken = default);

    Task<List<T>> InsertManyAsync<T>(string table, IEnumerable<object> payload, CancellationToken cancellationToken = default);

    Task<T?> UpsertSingleAsync<T>(
        string table,
        object payload,
        string onConflictColumn,
        CancellationToken cancellationToken = default);

    Task<T?> UpdateSingleAsync<T>(
        string table,
        IReadOnlyDictionary<string, string> query,
        object payload,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string table, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken = default);

    Task<T?> RpcAsync<T>(string functionName, object? payload = null, CancellationToken cancellationToken = default);
}
