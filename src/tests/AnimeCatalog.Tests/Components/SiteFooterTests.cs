using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class SiteFooterTests
{
    [Fact]
    public void RendersSourceAndBuildLinks()
    {
        using var context = new BunitContext();

        var cut = context.Render<SiteFooter>();

        var source = cut.Find(".site-footer__link");
        Assert.Equal(BuildInfo.SourceRepositoryUrl, source.GetAttribute("href"));
        Assert.Equal("Open source code on GitHub", source.GetAttribute("aria-label"));

        var commit = cut.Find(".site-footer__commit");
        Assert.Equal(BuildInfo.CommitUrl, commit.GetAttribute("href"));
        Assert.Equal(BuildInfo.ShortCommitSha, commit.TextContent.Trim());
    }
}
