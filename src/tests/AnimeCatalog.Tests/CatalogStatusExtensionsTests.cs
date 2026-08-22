using AnimeCatalog.Models;

namespace AnimeCatalog.Tests;

public sealed class CatalogStatusExtensionsTests
{
    [Fact]
    public void Parse_RoundTripsSnakeCaseValues()
    {
        foreach (var status in Enum.GetValues<CatalogStatus>())
        {
            var apiValue = status.ToApiValue();
            Assert.Equal(status, CatalogStatusExtensions.Parse(apiValue));
        }
    }

    // Parse still throws, because a row the database handed back with an unknown status is a real
    // problem. A ?status= somebody edited by hand is not, and it reaches the catalog page through a
    // lifecycle method where a throw has no boundary to land in.
    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsUnknownValuesInsteadOfThrowing(string? value)
    {
        Assert.False(CatalogStatusExtensions.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_AcceptsPaddingAndTheWrongCase()
    {
        Assert.True(CatalogStatusExtensions.TryParse(" On_Hold ", out var status));
        Assert.Equal(CatalogStatus.OnHold, status);
    }
}
