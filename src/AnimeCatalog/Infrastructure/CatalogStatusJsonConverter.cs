using System.Text.Json;
using System.Text.Json.Serialization;
using AnimeCatalog.Models;

namespace AnimeCatalog.Infrastructure;

public sealed class CatalogStatusJsonConverter : JsonConverter<CatalogStatus>
{
    public override CatalogStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? string.Empty;
        return CatalogStatusExtensions.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, CatalogStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToApiValue());
    }
}
