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

    public static CatalogStatus Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "planned" => CatalogStatus.Planned,
        "watching" => CatalogStatus.Watching,
        "completed" => CatalogStatus.Completed,
        "on_hold" => CatalogStatus.OnHold,
        "dropped" => CatalogStatus.Dropped,
        _ => throw new FormatException($"Unsupported catalog status '{value}'.")
    };
}
