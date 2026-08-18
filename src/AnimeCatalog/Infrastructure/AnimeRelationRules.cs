namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Decides which AniList relations belong in an anime-only catalog.
/// </summary>
/// <remarks>
/// Shared by the detail pages and the admin add flow so the two can never drift: a rule that lived in
/// only one of them would let the source manga back into the other's suggestion list.
/// </remarks>
public static class AnimeRelationRules
{
    /// <summary>
    /// True when a relation is worth showing: an actual anime, not a theme song, and not one of the
    /// vague link types.
    /// </summary>
    public static bool IsRenderable(string? relationType, string? nodeType, string? nodeFormat) =>
        !IsWeakRelation(relationType) && IsAnimeType(nodeType) && !IsMusicFormat(nodeFormat);

    /// <summary>CHARACTER/OTHER carry almost no signal about what to watch next — usually a cameo.</summary>
    public static bool IsWeakRelation(string? relationType) =>
        relationType?.Trim().ToUpperInvariant() is "CHARACTER" or "OTHER";

    /// <summary>
    /// True when an edge may be followed while walking outward from a watched anime to find the rest
    /// of its franchise.
    /// </summary>
    /// <remarks>
    /// Two exclusions, for different reasons. CHARACTER and OTHER must never be traversed: a single
    /// crossover cameo would fuse unrelated franchises into one component and the walk would crawl a
    /// large part of AniList. ADAPTATION and SOURCE point at the manga or novel essentially always, so
    /// skipping them by relation type avoids fetching hundreds of ids only to discard them for being
    /// the wrong media type.
    /// </remarks>
    public static bool IsTraversable(string? relationType) =>
        relationType?.Trim().ToUpperInvariant() is
            "SEQUEL" or "PREQUEL" or "SIDE_STORY" or "PARENT" or "SPIN_OFF"
            or "ALTERNATIVE" or "SUMMARY" or "COMPILATION" or "CONTAINS";

    /// <summary>
    /// Theme songs and AMVs are <c>type: ANIME</c> with <c>format: MUSIC</c>, so type alone does not
    /// exclude them.
    /// </summary>
    public static bool IsMusicFormat(string? format) =>
        string.Equals(format?.Trim(), "MUSIC", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Anything not positively identified as anime is rejected. Unknown is the fail-safe answer: a
    /// manga must never leak in, and the cost is only that an unclassifiable relation stays hidden.
    /// </summary>
    public static bool IsAnimeType(string? type) =>
        string.Equals(type?.Trim(), "ANIME", StringComparison.OrdinalIgnoreCase);
}
