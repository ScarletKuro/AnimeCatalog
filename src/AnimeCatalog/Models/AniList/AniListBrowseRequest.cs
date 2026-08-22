using System.Globalization;
using System.Text;

namespace AnimeCatalog.Models.AniList;

/// <summary>
/// The archive's query shape: which season, sorted how, narrowed by what.
/// </summary>
/// <remarks>
/// Every enum-valued filter is a raw AniList string rather than a C# enum. JsonDefaults.Web
/// registers a camelCase JsonStringEnumConverter, so an enum would serialise into the GraphQL
/// variables as "popularityDesc" or "tvShort" and AniList would reject it. The existing
/// AniListMedia.Format / Season / Status properties are typed as strings for the same reason.
/// </remarks>
public sealed record AniListBrowseRequest
{
    public int? SeasonYear { get; init; }

    /// <summary>WINTER, SPRING, SUMMER or FALL - or null for the whole year.</summary>
    public string? Season { get; init; }

    public string Sort { get; init; } = "POPULARITY_DESC";

    public IReadOnlyList<string> Formats { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string? CountryOfOrigin { get; init; }

    /// <summary>
    /// Defaults to excluding adult titles. Passed to AniList rather than filtered client-side, so a
    /// page of results is never spent on entries that will not render. Null shows everything.
    /// </summary>
    public bool? IsAdult { get; init; } = false;

    public int? MinimumAverageScore { get; init; }

    public string? Search { get; init; }

    /// <summary>
    /// Canonical cache key.
    /// </summary>
    /// <remarks>
    /// The list-valued filters are sorted before they go in, so two requests a visitor would call
    /// identical - the same two formats picked in either order - can never produce two keys and two
    /// paid requests against a shared rate limit.
    /// </remarks>
    public string CacheSignature()
    {
        var builder = new StringBuilder("browse|");
        builder.Append(SeasonYear?.ToString(CultureInfo.InvariantCulture) ?? "*").Append('|');
        builder.Append(Season ?? "*").Append('|');
        builder.Append(Sort).Append('|');
        builder.Append(string.Join(',', Formats.Order(StringComparer.Ordinal))).Append('|');
        builder.Append(string.Join(',', Genres.Order(StringComparer.Ordinal))).Append('|');
        builder.Append(CountryOfOrigin ?? "*").Append('|');
        builder.Append(IsAdult?.ToString() ?? "*").Append('|');
        builder.Append(MinimumAverageScore?.ToString(CultureInfo.InvariantCulture) ?? "*").Append('|');
        builder.Append(Search?.Trim().ToLowerInvariant() ?? "*");

        return builder.ToString();
    }
}
