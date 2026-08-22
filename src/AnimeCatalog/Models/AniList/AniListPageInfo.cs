using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.AniList;

/// <summary>
/// AniList's paging cursor. Only <see cref="HasNextPage"/> is trustworthy.
/// </summary>
/// <remarks>
/// <c>total</c> and <c>lastPage</c> are deliberately not modelled. On page 1 of a season browse
/// AniList reported total 5000 and lastPage 100; page 6 of the very same query reported total 250
/// and lastPage 6 with an empty result set. Any result count or progress bar built on either is
/// fiction, so this type does not offer them - which is cheaper than repeatedly explaining why they
/// must not be used.
/// <para>
/// Page until <see cref="HasNextPage"/> is false, and treat an empty page as the end regardless:
/// AniList reports hasNextPage true on the page past the last one.
/// </para>
/// </remarks>
public sealed class AniListPageInfo
{
    [JsonPropertyName("currentPage")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; set; }
}
