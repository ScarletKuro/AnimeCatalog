using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeCatalog.Infrastructure;

public static class JsonDefaults
{
    public static JsonSerializerOptions Web { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    /// <summary>
    /// Options for request payloads that name every column they own: nulls are written out rather
    /// than dropped, so clearing a value (an unrated score, a removed franchise) reaches the API.
    /// </summary>
    /// <remarks>
    /// A PostgREST <c>merge-duplicates</c> upsert or a PATCH only touches the columns present in
    /// the body, so a dropped null reads as "leave it alone" and the old value survives the save.
    /// </remarks>
    public static JsonSerializerOptions Payload { get; } = Create(JsonIgnoreCondition.Never);

    private static JsonSerializerOptions Create(JsonIgnoreCondition ignoreCondition)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = ignoreCondition
        };

        options.Converters.Add(new CatalogStatusJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
