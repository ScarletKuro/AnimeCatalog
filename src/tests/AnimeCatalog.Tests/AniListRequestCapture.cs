using System.Net;
using System.Text;
using System.Text.Json;
using AnimeCatalog.Options;
using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

/// <summary>
/// Runs one <see cref="AniListService"/> call against a stub and hands back what went out.
/// </summary>
/// <remarks>
/// The variables are exposed alongside the document because half the risk lives there. The
/// anonymous-type property names have to match the <c>$variable</c> names the document declares, and
/// a rename compiles perfectly while failing at runtime with "variable not provided" - which is
/// exactly the class of bug no hand-built fixture can catch.
/// </remarks>
internal static class AniListRequestCapture
{
    internal const string EmptyPage = """{"data":{"Page":{"media":[]}}}""";

    internal sealed record CapturedRequest(string Document, JsonElement Variables)
    {
        /// <summary>Whether the variables object carries the property at all - not whether it is null.</summary>
        public bool HasVariable(string name) => Variables.TryGetProperty(name, out _);

        public JsonElement Variable(string name)
        {
            Assert.True(Variables.TryGetProperty(name, out var value), $"'{name}' should be sent as a variable");
            return value;
        }
    }

    public static async Task<CapturedRequest> CaptureAsync(
        Func<AniListService, Task> call,
        string responseJson = EmptyPage)
    {
        var handler = new CapturingHandler(responseJson);
        var service = new AniListService(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new AniListOptions { GraphQlUrl = "https://graphql.anilist.co" }));

        await call(service);

        Assert.NotNull(handler.Body);

        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;

        // The document travels as a JSON string, so newlines arrive escaped.
        var query = root.GetProperty("query").GetString() ?? string.Empty;

        // Cloned so the value outlives the JsonDocument this using block disposes.
        var variables = root.TryGetProperty("variables", out var raw)
            ? raw.Clone()
            : default;

        return new CapturedRequest(query, variables);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
