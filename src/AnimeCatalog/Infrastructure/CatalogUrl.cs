using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// The catalog page's four pieces of state - search text, status, sort and page - as an address,
/// and back again.
/// </summary>
/// <remarks>
/// Hand-built rather than NavigationManager.GetUriWithQueryParameters, for the same reasons
/// <see cref="LoginLink"/> is: the result has to be base-relative for the sub-path the app is
/// served from, unrelated query parameters must be dropped rather than carried along, and the
/// parameter order has to be fixed. That last one is load-bearing - it makes the canonical form a
/// total function of the four values, which is what lets Catalog.razor compare the address it was
/// given against the address it wants and converge in a single redirect.
/// </remarks>
public static class CatalogUrl
{
    private const string Path = "catalog";

    /// <summary>The canonical address for these four values. Defaults are left out entirely.</summary>
    public static string For(string query, CatalogStatus? status, CatalogSortOption sort, int page)
    {
        var parts = new List<string>(4);
        Append(parts, "q", QueryText(query), escape: true);
        Append(parts, "status", StatusText(status), escape: false);
        Append(parts, "sort", SortText(sort), escape: false);
        Append(parts, "page", PageText(page), escape: false);

        return parts.Count == 0 ? Path : $"{Path}?{string.Join('&', parts)}";
    }

    // Each of the four returns null for its default value, so a catalog nobody has filtered stays a
    // clean "catalog" rather than growing "?q=&sort=Title&page=1". These are also what the page
    // compares its raw parameters against, so they are the definition of canonical, not a cosmetic
    // tidy-up.

    /// <summary>
    /// Tested on length rather than with IsNullOrWhiteSpace: this is the raw text the search box
    /// renders, and a visitor who has typed one space has typed something. BuildCatalog trims when
    /// it matches, which is where trimming belongs - doing it here would fight the caret.
    /// </summary>
    public static string? QueryText(string query) => query.Length == 0 ? null : query;

    public static string? StatusText(CatalogStatus? status) => status?.ToApiValue();

    public static string? SortText(CatalogSortOption sort) =>
        sort == CatalogSortOption.Title ? null : sort.ToString();

    public static string? PageText(int page) => page <= 1 ? null : page.ToString();

    /// <summary>Never trimmed, and never null: an absent ?q= and an empty one are the same box.</summary>
    public static string ParseQuery(string? raw) => raw ?? string.Empty;

    /// <summary>An unknown status is dropped rather than thrown, and reads as "All".</summary>
    public static CatalogStatus? ParseStatus(string? raw) =>
        CatalogStatusExtensions.TryParse(raw, out var status) ? status : null;

    /// <summary>
    /// ignoreCase so a hand-typed ?sort=year works, and IsDefined because Enum.TryParse happily
    /// accepts "99" and hands back a value no switch arm matches.
    /// </summary>
    public static CatalogSortOption ParseSort(string? raw) =>
        Enum.TryParse<CatalogSortOption>(raw, ignoreCase: true, out var sort) && Enum.IsDefined(sort)
            ? sort
            : CatalogSortOption.Title;

    /// <summary>
    /// Garbage, zero, a negative and an overflowing number all read as the first page. Only the
    /// lower bound can be applied here: the upper one needs a list to count, which the page does
    /// not have until its load resolves.
    /// </summary>
    public static int ParsePage(string? raw) =>
        int.TryParse(raw, out var page) ? Math.Max(1, page) : 1;

    private static void Append(List<string> parts, string name, string? value, bool escape)
    {
        if (value is null)
        {
            return;
        }

        parts.Add($"{name}={(escape ? Uri.EscapeDataString(value) : value)}");
    }
}
