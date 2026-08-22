namespace AnimeCatalog.Models.AniList;

/// <summary>
/// One page of a paged AniList query, with "is there more" already reconciled.
/// </summary>
/// <remarks>
/// <see cref="HasNextPage"/> is <c>pageInfo.hasNextPage</c> AND-ed with "this page actually returned
/// something". The empty-page half is what terminates a walk: AniList reports hasNextPage true on
/// the page past the last one, so trusting the flag alone runs every browse to its page cap.
/// </remarks>
public sealed record AniListPageResult<T>(IReadOnlyList<T> Items, int Page, bool HasNextPage)
{
    public static AniListPageResult<T> Empty(int page) => new([], page, false);
}
