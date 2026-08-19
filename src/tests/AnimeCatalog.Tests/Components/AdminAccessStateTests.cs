using System.Net;
using System.Text.Json;
using AnimeCatalog.Components;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Options;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Components;

public sealed class AdminAccessStateTests
{
    private const string SessionStorageKey = "animeCatalog.auth.session";

    [Fact]
    public async Task AnonymousVisitor_IsAskedToSignInAndComesBackToTheSamePage()
    {
        await using var context = CreateContext(withStoredSession: false);

        var cut = context.Render<AdminAccessState>(parameters => parameters
            .Add(p => p.Purpose, "adding anime"));

        Assert.Equal("Authentication required", cut.Find("h3").TextContent);
        Assert.Contains("before adding anime.", cut.Find(".access-card__body").TextContent);
        Assert.Contains("login?returnUrl=admin%2Fadd", cut.Find(".button-row a").GetAttribute("href"));
    }

    [Fact]
    public async Task SignedInNonAdmin_GetsNoSignInPromptAndAWayOut()
    {
        await using var context = CreateContext(withStoredSession: true);
        await context.Services.GetRequiredService<AuthService>().InitializeAsync();

        var cut = context.Render<AdminAccessState>();

        // Offering "Sign in" to somebody already signed in is the dead end this component exists to
        // avoid: only a different account, or an app_admins row, can fix this.
        Assert.Equal("Admin access required", cut.Find("h3").TextContent);
        Assert.DoesNotContain("returnUrl", cut.Markup);
        Assert.EndsWith("catalog", cut.Find(".button-row a").GetAttribute("href"));
        Assert.Equal("Sign out", cut.Find(".button-row button").TextContent.Trim());
    }

    [Fact]
    public async Task SigningOut_FlipsTheCardBackToTheSignInPrompt()
    {
        await using var context = CreateContext(withStoredSession: true);
        var authService = context.Services.GetRequiredService<AuthService>();
        await authService.InitializeAsync();

        var cut = context.Render<AdminAccessState>();
        cut.Find(".button-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.False(authService.IsAuthenticated);
            Assert.Equal("Authentication required", cut.Find("h3").TextContent);
        });
    }

    private static BunitContext CreateContext(bool withStoredSession)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        if (withStoredSession)
        {
            var module = context.JSInterop.SetupModule("./js/auth.js");
            module.Setup<string?>("getItem", SessionStorageKey).SetResult(SerializeUnexpiredSession());
        }

        context.Services.AddSingleton<NavigationManager>(
            new TestNavigationManager("https://localhost:7227/", "https://localhost:7227/admin/add"));
        context.Services.AddSingleton(sp => new AuthService(
            new HttpClient(new StubNonAdminHandler()),
            new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
            sp.GetRequiredService<NavigationManager>(),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "sb_publishable_123"
            }),
            new AppAuthenticationStateProvider()));

        return context;
    }

    private static string SerializeUnexpiredSession()
    {
        return JsonSerializer.Serialize(
            new AuthSession
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
                User = new AppUser { Id = "11111111-1111-1111-1111-111111111111" }
            },
            JsonDefaults.Web);
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

    private sealed class StubNonAdminHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            // is_admin answers "no", and the sign-out call has to succeed for the card to flip.
            if (path.EndsWith("/rest/v1/rpc/is_admin", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("false")
                });
            }

            if (path.EndsWith("/auth/v1/logout", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        }
    }
}
