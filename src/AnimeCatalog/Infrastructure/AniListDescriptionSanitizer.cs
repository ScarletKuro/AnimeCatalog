using System.Text;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Turns an AniList synopsis into markup that is safe to hand to <c>MarkupString</c>.
/// </summary>
/// <remarks>
/// AniList descriptions are community-editable and arrive with real HTML in them
/// (<c>&lt;br&gt;</c>, <c>&lt;i&gt;</c>, <c>&lt;a&gt;</c>) even when requested with
/// <c>asHtml: false</c>. Rather than filtering dangerous markup out — a blocklist you can always
/// lose — this strips <em>all</em> tags, escapes the surviving text, and then rebuilds the markup
/// from literals we emit ourselves. Nothing attacker-controlled can reach the output as markup, so
/// <c>&lt;script&gt;</c>, <c>onerror=</c> and <c>javascript:</c> hrefs all degrade to visible text.
/// The cost is losing italics and bold from synopses, which is a fair trade for a provable path.
/// </remarks>
public static class AniListDescriptionSanitizer
{
    private const string ParagraphBreak = "\n\n";

    public static SanitizedDescription Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SanitizedDescription.Empty;
        }

        var plainText = ToPlainText(raw);

        return plainText.Length == 0
            ? SanitizedDescription.Empty
            : new SanitizedDescription(BuildHtml(plainText), plainText);
    }

    /// <summary>
    /// Strips every tag and spoiler marker, leaving only the readable text plus newlines.
    /// </summary>
    public static string ToPlainText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var withoutMarkup = StripMarkup(raw);
        var withoutSpoilerMarkers = StripSpoilerMarkers(withoutMarkup);

        return CollapseWhitespace(HtmlEntities.Decode(withoutSpoilerMarkers));
    }

    /// <summary>
    /// Truncates plain text on a word boundary, appending an ellipsis when anything was cut.
    /// </summary>
    public static string Truncate(string? plainText, int maxChars)
    {
        if (string.IsNullOrEmpty(plainText) || maxChars <= 0 || plainText.Length <= maxChars)
        {
            return plainText ?? string.Empty;
        }

        var window = plainText[..maxChars];
        var lastBreak = window.LastIndexOfAny([' ', '\n', '\t']);

        // A single word longer than the whole window has no boundary to break on, so hard-cut it.
        var cut = lastBreak > 0 ? window[..lastBreak] : window;

        return cut.TrimEnd(' ', '\n', '\t', ',', ';', ':', '.', '-') + "…";
    }

    // Removes every tag in one pass, keeping only the structure worth preserving: <br> becomes a
    // line break and paragraph-ish boundaries become blank lines. Inner text survives because it
    // lives outside the angle brackets.
    private static string StripMarkup(string value)
    {
        var builder = new StringBuilder(value.Length);
        var index = 0;

        while (index < value.Length)
        {
            var character = value[index];

            // A '<' only opens a tag when a name, a closing slash or a declaration follows it.
            // Prose like "5 < 7" must stay literal text, which is why this is not just IndexOf('>').
            if (character != '<' || !IsTagStart(value, index))
            {
                builder.Append(character);
                index++;
                continue;
            }

            var close = value.IndexOf('>', index);
            if (close < 0)
            {
                // Unterminated tag at end of input: drop the fragment rather than leak markup.
                break;
            }

            var name = ReadTagName(value, index + 1);

            if (name is "br")
            {
                builder.Append('\n');
            }
            else if (name is "p" or "div" or "li" or "ul" or "ol" or "blockquote")
            {
                builder.Append(ParagraphBreak);
            }

            index = close + 1;
        }

        return builder.ToString();
    }

    private static bool IsTagStart(string value, int index)
    {
        var next = index + 1;
        if (next >= value.Length)
        {
            return false;
        }

        return char.IsAsciiLetter(value[next]) || value[next] is '/' or '!' or '?';
    }

    private static string ReadTagName(string value, int start)
    {
        var index = start;

        if (index < value.Length && value[index] == '/')
        {
            index++;
        }

        var nameStart = index;
        while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] == '-'))
        {
            index++;
        }

        return value[nameStart..index].ToLowerInvariant();
    }

    // AniList wraps spoilers as ~!hidden text!~. The markers are noise once the text is inlined.
    private static string StripSpoilerMarkers(string value) =>
        value.Replace("~!", string.Empty, StringComparison.Ordinal)
             .Replace("!~", string.Empty, StringComparison.Ordinal);

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingNewlines = 0;
        var started = false;

        foreach (var character in value)
        {
            if (character is '\n' or '\r')
            {
                if (character == '\n')
                {
                    pendingNewlines++;
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (pendingNewlines == 0 && started && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (started && pendingNewlines > 0)
            {
                // Two or more newlines mean a paragraph break; one means a line break.
                builder.Append(pendingNewlines >= 2 ? ParagraphBreak : "\n");
            }

            pendingNewlines = 0;
            builder.Append(character);
            started = true;
        }

        return builder.ToString().Trim();
    }

    // Every character of `plainText` is escaped before any markup is added, so the only tags in
    // the result are the <p> and <br /> literals written here.
    private static string BuildHtml(string plainText)
    {
        var builder = new StringBuilder(plainText.Length + 16);
        var paragraphs = plainText.Split(ParagraphBreak, StringSplitOptions.RemoveEmptyEntries);

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            builder.Append("<p>");

            var lines = trimmed.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append("<br />");
                }

                Escape(builder, lines[index]);
            }

            builder.Append("</p>");
        }

        return builder.ToString();
    }

    private static void Escape(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&#39;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }
    }

    // Descriptions contain a handful of entities (&amp;, &quot;, &#39;). Decoding them here means
    // the plain-text form reads correctly; BuildHtml re-escapes before anything becomes markup.
    private static class HtmlEntities
    {
        public static string Decode(string value)
        {
            if (!value.Contains('&'))
            {
                return value;
            }

            return value
                .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
                .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
                .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
                .Replace("&#39;", "'", StringComparison.Ordinal)
                .Replace("&apos;", "'", StringComparison.OrdinalIgnoreCase)
                .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed record SanitizedDescription(string Html, string PlainText)
{
    public static readonly SanitizedDescription Empty = new(string.Empty, string.Empty);

    public bool HasContent => PlainText.Length > 0;
}
