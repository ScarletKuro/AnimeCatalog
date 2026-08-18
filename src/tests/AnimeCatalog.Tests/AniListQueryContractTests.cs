using System.Net;
using System.Text;
using AnimeCatalog.Options;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// Asserts the enrichment query actually asks for every field the app reads off the response.
/// </summary>
/// <remarks>
/// <para>
/// A field the code depends on but the document never requests deserializes as null, which no unit
/// test with hand-built fixtures can catch — they set the property directly. That is exactly how the
/// franchise-gap walk shipped broken: <c>type</c> was requested on relation nodes but not on the media
/// itself, so every fetched title looked like "not an anime" and the walk expanded through nothing.
/// </para>
/// <para>
/// The selection set is parsed by brace depth rather than searched as a string. A substring check is
/// worthless here: <c>rankings { rank type format ... }</c> and <c>relationType</c> both contain the
/// text being looked for, so a naive assertion passes even with the field missing.
/// </para>
/// </remarks>
public sealed class AniListQueryContractTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("type")]          // gates the whole relation walk via AnimeRelationRules.IsAnimeType
    [InlineData("format")]        // separates a theme song from a real entry
    [InlineData("status")]
    [InlineData("averageScore")]  // the ranking the suggestions page sorts by
    [InlineData("popularity")]    // picks the flagship title when naming a franchise
    [InlineData("siteUrl")]
    [InlineData("bannerImage")]
    [InlineData("coverImage")]
    [InlineData("description")]
    [InlineData("relations")]
    public async Task EnrichmentQuery_AsksForEveryFieldTheAppReadsOffTheMediaItself(string field)
    {
        var fields = await MediaFieldsAsync();

        Assert.Contains(field, fields);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]    // a SOURCE relation points at the manga; without this it looks like anime
    [InlineData("format")]  // MUSIC entries are type ANIME, so type alone is not enough
    [InlineData("title")]
    public async Task EnrichmentQuery_AsksForEveryFieldTheAppReadsOffARelationNode(string field)
    {
        var body = await CaptureRequestBodyAsync();

        var relations = SelectionOf(body, MediaSelectionStart(body), "relations");
        var edges = SelectionOf(body, relations, "edges");
        var node = SelectionOf(body, edges, "node");

        Assert.Contains(field, ImmediateFields(body, node));
    }

    [Fact]
    public async Task EnrichmentQuery_RequestsVersionedEnumsSoRetiredValuesAreNotReturned()
    {
        var body = await CaptureRequestBodyAsync();

        // v1 status still returns NOT_YET_AIRED, which is no longer a MediaStatus member.
        Assert.Contains("status(version: 2)", body);
        Assert.Contains("source(version: 3)", body);
        Assert.Contains("relationType(version: 2)", body);
    }

    private static async Task<IReadOnlyCollection<string>> MediaFieldsAsync()
    {
        var body = await CaptureRequestBodyAsync();
        return ImmediateFields(body, MediaSelectionStart(body));
    }

    /// <summary>Index of the brace opening the enrichment fragment's own selection set.</summary>
    private static int MediaSelectionStart(string body)
    {
        var fragment = body.IndexOf("fragment EnrichFields on Media", StringComparison.Ordinal);
        Assert.True(fragment >= 0, "the enrichment fragment should be part of the request");

        return body.IndexOf('{', fragment);
    }

    /// <summary>Index of the brace opening <paramref name="field"/>'s selection set, one level in.</summary>
    private static int SelectionOf(string body, int openBrace, string field)
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
    /// Field names selected directly inside a brace — nested selections and argument lists are skipped,
    /// so a <c>type</c> buried in <c>rankings</c> cannot be mistaken for the media's own.
    /// </summary>
    private static IReadOnlyCollection<string> ImmediateFields(string body, int openBrace)
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

    private static async Task<string> CaptureRequestBodyAsync()
    {
        var handler = new CapturingHandler();
        var service = new AniListService(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new AniListOptions { GraphQlUrl = "https://graphql.anilist.co" }));

        await service.GetEnrichedAnimeByIdsAsync([1]);

        Assert.NotNull(handler.Body);

        // The document travels as a JSON string, so newlines arrive escaped.
        return handler.Body!.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"Page":{"media":[]}}}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
