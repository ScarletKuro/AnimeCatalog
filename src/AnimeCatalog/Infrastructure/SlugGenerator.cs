using System.Text;
using System.Text.RegularExpressions;

namespace AnimeCatalog.Infrastructure;

public static partial class SlugGenerator
{
    public static string Generate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Guid.NewGuid().ToString("N")[..10];
        }

        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) || character is '-' or '_' or ':')
            {
                builder.Append('-');
            }
        }

        var collapsed = DuplicateDashRegex().Replace(builder.ToString(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(collapsed)
            ? Guid.NewGuid().ToString("N")[..10]
            : collapsed;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex DuplicateDashRegex();
}
