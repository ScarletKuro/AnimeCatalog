using System.Text.Json.Serialization;

namespace AnimeCatalog.Models.Supabase;

public sealed class AppSettingsRow
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("public_catalog_enabled")]
    public bool PublicCatalogEnabled { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
