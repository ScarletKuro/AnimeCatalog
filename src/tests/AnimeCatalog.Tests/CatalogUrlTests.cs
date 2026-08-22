using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.ViewModels;

namespace AnimeCatalog.Tests;

public sealed class CatalogUrlTests
{
    [Fact]
    public void For_OmitsEveryDefaultSoACleanCatalogStaysClean()
    {
        Assert.Equal("catalog", CatalogUrl.For(string.Empty, null, CatalogSortOption.Title, 1));
    }

    // The property the page's single-pass redirect rests on: the canonical text of a set of values
    // has to be one fixed string, or comparing the address it was given against the address it wants
    // would flip between two spellings forever.
    [Fact]
    public void For_OrdersTheParametersDeterministically()
    {
        Assert.Equal(
            "catalog?q=steins&status=watching&sort=Year&page=3",
            CatalogUrl.For("steins", CatalogStatus.Watching, CatalogSortOption.Year, 3));
    }

    [Fact]
    public void For_EscapesTheQueryText()
    {
        Assert.Equal("catalog?q=a%26b%3Dc", CatalogUrl.For("a&b=c", null, CatalogSortOption.Title, 1));
    }

    // A space is something the visitor typed, so it has to survive the round trip through the
    // address - BuildCatalog is what trims, when it matches.
    [Fact]
    public void For_KeepsAQueryOfNothingButWhitespace()
    {
        Assert.Equal("catalog?q=%20", CatalogUrl.For(" ", null, CatalogSortOption.Title, 1));
    }

    [Fact]
    public void Parse_RoundTripsEverythingForBuilds()
    {
        CatalogStatus?[] statuses = [null, .. Enum.GetValues<CatalogStatus>().Cast<CatalogStatus?>()];

        foreach (var sort in Enum.GetValues<CatalogSortOption>())
        {
            foreach (var status in statuses)
            {
                Assert.Equal(sort, CatalogUrl.ParseSort(CatalogUrl.SortText(sort)));
                Assert.Equal(status, CatalogUrl.ParseStatus(CatalogUrl.StatusText(status)));
            }
        }

        Assert.Equal("steins;gate", CatalogUrl.ParseQuery(CatalogUrl.QueryText("steins;gate")));
        Assert.Equal(7, CatalogUrl.ParsePage(CatalogUrl.PageText(7)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("abc")]
    [InlineData("3.0")]
    [InlineData("99999999999999999999")]
    public void ParsePage_TreatsZeroNegativeAndGarbageAsTheFirstPage(string? raw)
    {
        Assert.Equal(1, CatalogUrl.ParsePage(raw));
    }

    // Enum.TryParse accepts the underlying number and hands back a value no switch arm matches, so
    // "?sort=99" would sort by nothing at all without the IsDefined check.
    [Theory]
    [InlineData("99")]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseSort_FallsBackToTitleForAnythingItDoesNotRecognise(string? raw)
    {
        Assert.Equal(CatalogSortOption.Title, CatalogUrl.ParseSort(raw));
    }

    [Fact]
    public void ParseSort_AcceptsTheWrongCase()
    {
        Assert.Equal(CatalogSortOption.Year, CatalogUrl.ParseSort("year"));
    }
}
