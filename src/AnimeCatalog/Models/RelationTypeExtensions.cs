namespace AnimeCatalog.Models;

public static class RelationTypeExtensions
{
    // AniList can add MediaRelation members at any time and the value also reaches us straight
    // from the anime_relations table, so unlike CatalogStatusExtensions.Parse this never throws:
    // anything unrecognised falls back to a title-cased version of the raw value.
    public static string ToDisplayLabel(this string? relationType)
    {
        if (string.IsNullOrWhiteSpace(relationType))
        {
            return "Related";
        }

        return relationType.Trim().ToUpperInvariant() switch
        {
            // Synthesised by FranchiseService, not an AniList MediaRelation member.
            "SAME_FRANCHISE" => "Same franchise",
            "SEQUEL" => "Sequel",
            "PREQUEL" => "Prequel",
            "PARENT" => "Parent story",
            "SIDE_STORY" => "Side story",
            "SUMMARY" => "Summary",
            "ALTERNATIVE" => "Alternative",
            "SPIN_OFF" => "Spin-off",
            "ADAPTATION" => "Adaptation",
            "CHARACTER" => "Shared character",
            "SOURCE" => "Source",
            "COMPILATION" => "Compilation",
            "CONTAINS" => "Contains",
            "OTHER" => "Other",
            _ => Humanize(relationType)
        };
    }

    private static string Humanize(string value)
    {
        var words = value
            .Trim()
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "Related";
        }

        var result = words
            .Select(static (word, index) => index == 0
                ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()
                : word.ToLowerInvariant());

        return string.Join(' ', result);
    }
}
