namespace AnimeCatalog.Infrastructure;

/// <summary>
/// AniList is refusing or unreachable, as distinct from AniList answering with an error about the
/// query itself.
/// </summary>
/// <remarks>
/// <para>
/// AniList ships HTTP 403 with a body of
/// <c>{"errors":[{"message":"The AniList API has been temporarily disabled due to severe stability
/// issues.","status":403}]}</c> during its outages, and the same notice sometimes arrives on an HTTP
/// 200 with the status carried in the body instead.
/// </para>
/// <para>
/// Without this type both surfaced through <c>EnsureSuccessStatusCode</c> as a bare
/// <see cref="HttpRequestException"/>, whose message in a browser is "TypeError: Failed to fetch" —
/// indistinguishable from a CORS problem, a dropped connection, or a bug in this app. The visitor
/// cannot act on any of it, so the distinction that matters is "AniList is not answering" versus
/// "we sent a bad query", not the transport detail.
/// </para>
/// </remarks>
public sealed class AniListUnavailableException : Exception
{
    public const string DefaultMessage =
        "AniList is not answering right now, so titles and airing times cannot be loaded. " +
        "Nothing is wrong with your catalog - try again in a few minutes.";

    public AniListUnavailableException(int? statusCode, string? serverMessage, Exception? innerException = null)
        : base(DefaultMessage, innerException)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public int? StatusCode { get; }

    /// <summary>
    /// AniList's own words, when it sent any. Rendered as a secondary line, never as the primary
    /// message: it explains AniList's state, not what the visitor should do.
    /// </summary>
    public string? ServerMessage { get; }
}
