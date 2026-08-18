using AnimeCatalog.Components;
using AnimeCatalog.Models;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class StatusBadgeTests
{
    [Fact]
    public void RendersDisplayLabelAndCssClass()
    {
        using var context = new BunitContext();
        var cut = context.Render<StatusBadge>(parameters => parameters.Add(p => p.Status, CatalogStatus.OnHold));

        Assert.Contains("On Hold", cut.Markup);
        Assert.Contains("status-badge--on_hold", cut.Markup);
    }
}
