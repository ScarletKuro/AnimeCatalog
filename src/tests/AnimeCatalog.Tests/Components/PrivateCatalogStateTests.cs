using AnimeCatalog.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Components;

public sealed class PrivateCatalogStateTests
{
    [Fact]
    public void RendersPrivateMessageAndLoginLinkWithReturnUrl()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<NavigationManager>(new TestNavigationManager("https://localhost:7227/", "https://localhost:7227/catalog?status=watching"));

        var cut = context.Render<PrivateCatalogState>();

        Assert.Contains("Private catalog", cut.Markup);
        Assert.Contains("Sign in", cut.Markup);
        Assert.Contains("login?returnUrl=catalog%3Fstatus%3Dwatching", cut.Markup);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
