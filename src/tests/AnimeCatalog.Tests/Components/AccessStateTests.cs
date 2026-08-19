using AnimeCatalog.Components;
using AnimeCatalog.ViewModels;
using Bunit;

namespace AnimeCatalog.Tests.Components;

public sealed class AccessStateTests
{
    [Fact]
    public void RendersOneCardWithTheSuppliedCopyAndAction()
    {
        using var context = new BunitContext();

        var cut = context.Render<AccessState>(parameters => parameters
            .Add(p => p.Title, "Admin access required")
            .Add(p => p.Message, "Not in app_admins.")
            .Add(p => p.Action, "<a href=\"catalog\" class=\"button button--primary\">Back to catalog</a>"));

        // CatalogTests asserts exactly one .access-card per page, so the component must not nest cards.
        Assert.Single(cut.FindAll(".access-card"));
        Assert.Equal("Admin access required", cut.Find("h3").TextContent);
        Assert.Equal("Not in app_admins.", cut.Find(".access-card__body").TextContent);
        Assert.Equal("Back to catalog", cut.Find(".button-row a").TextContent);
    }

    [Fact]
    public void OmitsTheActionRowWhenThereIsNothingToOffer()
    {
        using var context = new BunitContext();

        var cut = context.Render<AccessState>(parameters => parameters
            .Add(p => p.Title, "Locked")
            .Add(p => p.Message, "Nothing to do here."));

        Assert.Empty(cut.FindAll(".button-row"));
    }

    [Fact]
    public void BadgeVariantsDrawDifferentGlyphs()
    {
        using var context = new BunitContext();

        var lockCard = context.Render<AccessState>(parameters => parameters
            .Add(p => p.Title, "Locked")
            .Add(p => p.Message, "Sign in.")
            .Add(p => p.Badge, AccessBadge.Lock));

        var shieldCard = context.Render<AccessState>(parameters => parameters
            .Add(p => p.Title, "Refused")
            .Add(p => p.Message, "Not an admin.")
            .Add(p => p.Badge, AccessBadge.Shield));

        // The two states are not interchangeable: a padlock reads "not signed in", a barred shield
        // reads "signed in and still not permitted".
        Assert.NotEqual(
            lockCard.Find(".access-card__badge svg").InnerHtml,
            shieldCard.Find(".access-card__badge svg").InnerHtml);
    }
}
