using System.Text.Json.Serialization;

namespace AnimeCatalog.Models;

public sealed class AuthSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAtUnixSeconds { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "bearer";

    [JsonPropertyName("user")]
    public AppUser User { get; set; } = new();

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnixSeconds);

    public bool IsExpired(DateTimeOffset now, TimeSpan? buffer = null)
    {
        var grace = buffer ?? TimeSpan.FromMinutes(1);
        return ExpiresAt <= now.Add(grace);
    }
}
