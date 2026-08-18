using AnimeCatalog.Pages;
using Bunit;

namespace AnimeCatalog.Tests.Pages;

public sealed class NotFoundTests
{
    [Fact]
    public void RendersTheHeadingRoutesFocusesAfterNavigation()
    {
        using var context = new BunitContext();

        var cut = context.Render<NotFound>();

        // Routes.razor focuses <FocusOnNavigate Selector="h1" />, so the h1 is load-bearing:
        // without it a 404 navigation leaves focus wherever the previous page left it.
        Assert.Equal("Page not found", cut.Find("h1").TextContent.Trim());
        Assert.Contains("The route you requested does not exist in this catalog.", cut.Markup);
    }

    [Fact]
    public void RendersTheLayoutHooksTheStylesheetTargets()
    {
        using var context = new BunitContext();

        var cut = context.Render<NotFound>();

        // The viewport centering, the glow and the numeral all hang off these class names.
        Assert.NotNull(cut.Find(".notfound-stage"));
        Assert.NotNull(cut.Find(".notfound-card"));
        Assert.NotNull(cut.Find(".notfound-card__glyph"));
        Assert.NotNull(cut.Find(".notfound-card__body"));
        Assert.NotNull(cut.Find(".notfound-card .button-row"));

        // The numeral repeats no information, so it must stay out of the accessibility tree.
        Assert.Equal("true", cut.Find(".notfound-card__glyph").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void BothActionsUseBaseRelativeLinks()
    {
        using var context = new BunitContext();

        var cut = context.Render<NotFound>();

        var hrefs = cut.FindAll(".button-row a")
            .Select(link => link.GetAttribute("href") ?? string.Empty)
            .ToArray();

        // Catalog plus the empty href that resolves to <base>, i.e. home. An origin-absolute
        // "/catalog" would escape the GitHub Pages sub-path - see SubPathNavigationTests.
        Assert.Equal(["catalog", string.Empty], hrefs);
    }
}
