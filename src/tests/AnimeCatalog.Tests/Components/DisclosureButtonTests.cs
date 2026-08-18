using AnimeCatalog.Components;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class DisclosureButtonTests
{
    [Fact]
    public void AriaExpandedReflectsState()
    {
        using var context = new BunitContext();

        var collapsed = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Show all"));
        Assert.Equal("false", collapsed.Find("button").GetAttribute("aria-expanded"));

        var expanded = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Show all")
            .Add(p => p.Expanded, true));
        Assert.Equal("true", expanded.Find("button").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Click_InvokesOnToggle()
    {
        using var context = new BunitContext();
        var toggled = false;

        var cut = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Show all")
            .Add(p => p.OnToggle, () => toggled = true));

        cut.Find("button").Click();

        Assert.True(toggled);
    }

    [Fact]
    public void LabelSwapsWithState()
    {
        using var context = new BunitContext();

        var cut = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Show all 14 entries")
            .Add(p => p.ExpandedLabel, "Show fewer entries")
            .Add(p => p.Expanded, true));

        Assert.Contains("Show fewer entries", cut.Markup);
        Assert.DoesNotContain("Show all 14 entries", cut.Markup);
    }

    [Fact]
    public void ExpandedLabelDefaultsToCollapsedLabel()
    {
        using var context = new BunitContext();

        var cut = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Toggle")
            .Add(p => p.Expanded, true));

        Assert.Contains("Toggle", cut.Markup);
    }

    [Fact]
    public void OpenModifierDrivesTheChevronRotation()
    {
        using var context = new BunitContext();

        var cut = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Toggle")
            .Add(p => p.Expanded, true));

        Assert.Contains("disclosure--open", cut.Markup);
        Assert.Equal("true", cut.Find(".disclosure__chevron").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void ControlsIdIsExposedAsAriaControls()
    {
        using var context = new BunitContext();

        var with = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Toggle")
            .Add(p => p.ControlsId, "entries-grid"));
        Assert.Equal("entries-grid", with.Find("button").GetAttribute("aria-controls"));

        var without = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Toggle"));
        Assert.Null(without.Find("button").GetAttribute("aria-controls"));
    }

    [Fact]
    public void IsATypeButtonSoItNeverSubmitsAForm()
    {
        using var context = new BunitContext();

        var cut = context.Render<DisclosureButton>(parameters => parameters
            .Add(p => p.CollapsedLabel, "Toggle"));

        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
    }
}
