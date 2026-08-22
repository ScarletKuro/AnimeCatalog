namespace AnimeCatalog.Tests;

/// <summary>
/// Reads an outgoing GraphQL document by brace depth.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from AniListQueryContractTests so the calendar documents can be held to the same
/// standard without a second copy of the parser. The logic is unchanged.
/// </para>
/// <para>
/// A substring check is worthless for this job: <c>rankings { rank type format ... }</c> and
/// <c>relationType</c> both contain the text a naive assertion would look for, so it passes even
/// with the field missing. Parsing by depth, and skipping argument lists, is what makes
/// "does the media itself select <c>type</c>" answerable.
/// </para>
/// </remarks>
internal static class GraphQlDocument
{
    /// <summary>Index of the brace opening a named fragment's own selection set.</summary>
    public static int FragmentSelectionStart(string body, string fragmentHeader)
    {
        var fragment = body.IndexOf(fragmentHeader, StringComparison.Ordinal);
        Assert.True(fragment >= 0, $"'{fragmentHeader}' should be part of the request");

        return body.IndexOf('{', fragment);
    }

    /// <summary>
    /// Index of the brace opening the operation's root selection set - the one holding <c>Page</c>
    /// or <c>Media</c>. Skips the variable declaration list, which is parenthesised.
    /// </summary>
    public static int OperationSelectionStart(string body)
    {
        var query = body.IndexOf("query", StringComparison.Ordinal);
        Assert.True(query >= 0, "the document should contain an operation");

        var index = query;
        var parens = 0;

        while (index < body.Length)
        {
            var character = body[index];

            if (character == '(') { parens++; index++; continue; }
            if (character == ')') { parens--; index++; continue; }
            if (character == '{' && parens == 0) { return index; }

            index++;
        }

        Assert.Fail("the operation should open a selection set");
        return -1;
    }

    /// <summary>Index of the brace opening <paramref name="field"/>'s selection set, one level in.</summary>
    public static int SelectionOf(string body, int openBrace, string field)
    {
        var index = openBrace + 1;
        var depth = 0;
        var parens = 0;

        while (index < body.Length)
        {
            var character = body[index];

            if (character == '(') { parens++; index++; continue; }
            if (character == ')') { parens--; index++; continue; }
            if (parens > 0) { index++; continue; }

            if (character == '{') { depth++; index++; continue; }
            if (character == '}')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
                index++;
                continue;
            }

            if (depth == 0 && char.IsAsciiLetter(character))
            {
                var start = index;
                while (index < body.Length && (char.IsAsciiLetterOrDigit(body[index]) || body[index] == '_'))
                {
                    index++;
                }

                if (body[start..index] == field)
                {
                    var brace = body.IndexOf('{', index);
                    Assert.True(brace >= 0, $"'{field}' should open a selection set");
                    return brace;
                }

                continue;
            }

            index++;
        }

        Assert.Fail($"'{field}' was not found in the selection set");
        return -1;
    }

    /// <summary>
    /// Field names selected directly inside a brace. Nested selections and argument lists are
    /// skipped, so a <c>type</c> buried in <c>rankings</c> cannot be mistaken for the media's own.
    /// </summary>
    public static IReadOnlyCollection<string> ImmediateFields(string body, int openBrace)
    {
        var fields = new List<string>();
        var index = openBrace + 1;
        var depth = 0;
        var parens = 0;

        while (index < body.Length)
        {
            var character = body[index];

            if (character == '(') { parens++; index++; continue; }
            if (character == ')') { parens--; index++; continue; }
            if (parens > 0) { index++; continue; }

            if (character == '{') { depth++; index++; continue; }
            if (character == '}')
            {
                if (depth == 0)
                {
                    return fields;
                }

                depth--;
                index++;
                continue;
            }

            if (depth == 0 && char.IsAsciiLetter(character))
            {
                var start = index;
                while (index < body.Length && (char.IsAsciiLetterOrDigit(body[index]) || body[index] == '_'))
                {
                    index++;
                }

                fields.Add(body[start..index]);
                continue;
            }

            index++;
        }

        return fields;
    }
}
