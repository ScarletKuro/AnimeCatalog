using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class ExpandableTextTests
{
    private static string LongHtml => "<p>" + new string('a', 500) + "</p>";

    [Fact]
    public void LongContent_StartsCollapsedWithAToggle()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, LongHtml));

        Assert.Contains("expandable-text__body--collapsed", cut.Markup);
        Assert.Equal("false", cut.Find(".disclosure").GetAttribute("aria-expanded"));
        Assert.Contains("Read more", cut.Markup);
    }

    [Fact]
    public void Toggle_ExpandsAndUpdatesAria()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, LongHtml));

        cut.Find(".disclosure").Click();

        Assert.DoesNotContain("expandable-text__body--collapsed", cut.Markup);
        Assert.Equal("true", cut.Find(".disclosure").GetAttribute("aria-expanded"));
        Assert.Contains("Show less", cut.Markup);
    }

    [Fact]
    public void ShortContent_RendersNoToggle()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Text, "A short synopsis."));

        Assert.Empty(cut.FindAll(".disclosure"));
        Assert.DoesNotContain("expandable-text__body--collapsed", cut.Markup);
    }

    [Fact]
    public void AriaControlsMatchesTheBodyId()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, LongHtml));

        var bodyId = cut.Find(".expandable-text__body").GetAttribute("id");
        Assert.False(string.IsNullOrWhiteSpace(bodyId));
        Assert.Equal(bodyId, cut.Find(".disclosure").GetAttribute("aria-controls"));
    }

    [Fact]
    public void Html_IsRenderedAsMarkupNotEscaped()
    {
        using var context = new BunitContext();

        // Already sanitized upstream; the component must not double-escape it.
        var html = "<p><br />Line" + new string('b', 450) + "</p>";

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, html));

        Assert.Contains("<br", cut.Markup);
        Assert.DoesNotContain("&lt;br", cut.Markup);
    }

    [Fact]
    public void Text_IsUsedWhenNoHtmlIsSupplied()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Text, "Manual grouping for related anime."));

        Assert.Contains("Manual grouping for related anime.", cut.Markup);
    }

    [Fact]
    public void CollapsedLines_SetsTheClampVariable()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, LongHtml)
            .Add(p => p.CollapsedLines, 4));

        Assert.Contains("--clamp-lines:4", cut.Markup);
    }

    [Fact]
    public void ToggleLabels_AreOverridable()
    {
        using var context = new BunitContext();

        var cut = context.Render<ExpandableText>(parameters => parameters
            .Add(p => p.Html, LongHtml)
            .Add(p => p.ExpandLabel, "More")
            .Add(p => p.CollapseLabel, "Less"));

        Assert.Contains("More", cut.Markup);
        cut.Find(".disclosure").Click();
        Assert.Contains("Less", cut.Markup);
    }
}
