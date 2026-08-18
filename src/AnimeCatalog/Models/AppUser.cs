using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeCatalog.Models;

public sealed class AppUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("user_metadata")]
    public Dictionary<string, object?> UserMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var displayName =
                GetMetadataString("user_name") ??
                GetMetadataString("name") ??
                GetMetadataString("full_name") ??
                GetMetadataString("preferred_username");

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            return Email ?? "User";
        }
    }

    [JsonIgnore]
    public string? AvatarUrl =>
        GetMetadataString("avatar_url") ??
        GetMetadataString("picture") ??
        GetMetadataString("avatar");

    private string? GetMetadataString(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!UserMetadata.TryGetValue(key, out var value))
            {
                continue;
            }

            var candidate = value switch
            {
                null => null,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                JsonElement json => json.ToString(),
                _ => value.ToString()
            };

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }
}
