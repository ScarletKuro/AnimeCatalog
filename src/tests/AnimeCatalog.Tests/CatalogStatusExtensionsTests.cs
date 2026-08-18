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
}
