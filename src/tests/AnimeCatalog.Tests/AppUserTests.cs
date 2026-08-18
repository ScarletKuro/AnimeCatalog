using System.Text.Json;
using AnimeCatalog.Models;

namespace AnimeCatalog.Tests;

public sealed class AppUserTests
{
    [Fact]
    public void DisplayNameAndAvatarUrl_AreReadFromUserMetadata()
    {
        var user = new AppUser
        {
            Email = "owner@example.com",
            UserMetadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["user_name"] = JsonDocument.Parse("\"ScarletKuro\"").RootElement.Clone(),
                ["avatar_url"] = JsonDocument.Parse("\"https://avatars.githubusercontent.com/u/42?v=4\"").RootElement.Clone()
            }
        };

        Assert.Equal("ScarletKuro", user.DisplayName);
        Assert.Equal("https://avatars.githubusercontent.com/u/42?v=4", user.AvatarUrl);
    }

    [Fact]
    public void DisplayName_FallsBackToEmail_WhenMetadataIsMissing()
    {
        var user = new AppUser
        {
            Email = "owner@example.com"
        };

        Assert.Equal("owner@example.com", user.DisplayName);
        Assert.Null(user.AvatarUrl);
    }
}
