namespace AnimeCatalog.Infrastructure;

public static class FranchiseTitleSuggester
{
    private static readonly char[] TrimCharacters = [' ', '\t', '\r', '\n', '-', '_', ':', ';', ',', '.', '!', '?', '/', '\\', '|', '(', ')', '[', ']', '{', '}'];

    public static string? Build(string? englishTitle, string? romajiTitle)
    {
        var displayTitle = string.IsNullOrWhiteSpace(englishTitle)
            ? romajiTitle?.Trim()
            : englishTitle.Trim();

        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            return null;
        }

        var candidate = displayTitle.Split(':', 2, StringSplitOptions.TrimEntries)[0].Trim(TrimCharacters);
        return string.IsNullOrWhiteSpace(candidate)
            ? displayTitle.Trim(TrimCharacters)
            : candidate;
    }
}
