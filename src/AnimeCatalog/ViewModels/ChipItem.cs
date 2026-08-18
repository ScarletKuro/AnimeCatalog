namespace AnimeCatalog.ViewModels;

/// <summary>
/// One chip in a <c>ChipList</c>: genres, tags with a relevance rank, studios with a count.
/// </summary>
/// <param name="Label">Visible text.</param>
/// <param name="Value">Optional trailing value, e.g. a count like "x4".</param>
/// <param name="Rank">Optional 0-100 relevance, rendered as a mini bar plus screen-reader text.</param>
/// <param name="Href">When set the chip becomes a link.</param>
/// <param name="Variant">neutral | accent | warm | success | danger | muted.</param>
/// <param name="IsExternal">Opens <paramref name="Href"/> in a new tab.</param>
public sealed record ChipItem(
    string Label,
    string? Value = null,
    int? Rank = null,
    string? Href = null,
    string Variant = "neutral",
    bool IsExternal = false);
