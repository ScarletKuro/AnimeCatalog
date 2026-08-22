namespace AnimeCatalog.Models;

public static class CatalogStatusExtensions
{
    public static string ToApiValue(this CatalogStatus status) => status switch
    {
        CatalogStatus.Planned => "planned",
        CatalogStatus.Watching => "watching",
        CatalogStatus.Completed => "completed",
        CatalogStatus.OnHold => "on_hold",
        CatalogStatus.Dropped => "dropped",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string ToDisplayLabel(this CatalogStatus status) => status switch
    {
        CatalogStatus.Planned => "Planned",
        CatalogStatus.Watching => "Watching",
        CatalogStatus.Completed => "Completed",
        CatalogStatus.OnHold => "On Hold",
        CatalogStatus.Dropped => "Dropped",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    /// <summary>
    /// For values that came from the database or from a select, where an unsupported status means
    /// something is wrong upstream and should fail loudly.
    /// </summary>
    public static CatalogStatus Parse(string value) => TryParse(value, out var status)
        ? status
        : throw new FormatException($"Unsupported catalog status '{value}'.");

    /// <summary>
    /// Non-throwing counterpart to <see cref="Parse"/>, for values that came from a URL rather than
    /// from a select. A hand-edited ?status= must not take the page down: query-parameter binding
    /// materialises inside SetParametersAsync, where a throw goes looking for an error boundary this
    /// app does not have and lands the visitor on the reload banner instead.
    /// </summary>
    public static bool TryParse(string? value, out CatalogStatus status)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "planned": status = CatalogStatus.Planned; return true;
            case "watching": status = CatalogStatus.Watching; return true;
            case "completed": status = CatalogStatus.Completed; return true;
            case "on_hold": status = CatalogStatus.OnHold; return true;
            case "dropped": status = CatalogStatus.Dropped; return true;
            default: status = default; return false;
        }
    }
}
