using System.Net;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Models;
using AnimeCatalog.Options;
using AnimeCatalog.Pages;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Pages;

public sealed class LoginTests
{
    private const string SessionStorageKey = "animeCatalog.auth.session";

    [Fact]
    public async Task SignInPrompt_OffersTheGitHubActionUnderTheFocusableHeading()
    {
        await using var context = CreateContext();

        var cut = context.Render<Login>();

        // Routes.razor focuses the h1 after navigation, so it has to survive every branch below too.
        Assert.Equal("Sign in to Anime Catalog", cut.Find("h1").TextContent);

        var signIn = cut.Find(".login-card__actions button");
        Assert.Contains("Sign in with GitHub", signIn.TextContent);
        Assert.False(signIn.HasAttribute("disabled"));
    }

    [Fact]
    public async Task UnconfiguredAuth_NamesTheMissingSettingsAndDisablesTheAction()
    {
        await using var context = CreateContext(configured: false);

        var cut = context.Render<Login>();

        Assert.Equal("Sign in to Anime Catalog", cut.Find("h1").TextContent);
        Assert.Contains("Supabase.PublishableKey", cut.Find(".panel__notice").TextContent);
        Assert.True(cut.Find(".login-card__actions button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task SignedInVisitor_GetsTheHeadingAndTheAdminShortcut()
    {
        await using var context = CreateContext(withStoredSession: true);

        // Login does not subscribe to StateChanged, so the session has to exist before the render.
        await context.Services.GetRequiredService<AuthService>().InitializeAsync();

        var cut = context.Render<Login>();

        Assert.Equal("You are signed in", cut.Find("h1").TextContent);
        Assert.Contains("octocat", cut.Find(".login-card__body").TextContent);
        Assert.EndsWith("admin", cut.Find(".login-card__actions a").GetAttribute("href"));
    }

    [Fact]
    public async Task FailedCallback_LeavesTheSignInActionAvailableToRetry()
    {
        await using var context = CreateContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("login?code=bogus");

        var cut = context.Render<Login>();

        // The failure used to replace the whole action with an ErrorState card, which left a failed
        // callback with nothing to click.
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Sign in to Anime Catalog", cut.Find("h1").TextContent);
            Assert.NotEmpty(cut.FindAll(".action-feedback--error"));

            var signIn = cut.Find(".login-card__actions button");
            Assert.Contains("Sign in with GitHub", signIn.TextContent);
            Assert.False(signIn.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task FailedCallback_KeepsTheRawPayloadBehindTheDisclosure()
    {
        await using var context = CreateContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("login?code=bogus");

        var cut = context.Render<Login>();

        // With no PKCE verifier in storage the exchange throws before any request is made, and that
        // exception message stands in for the Supabase error payload a real failure would carry.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.disclosure")));
        Assert.DoesNotContain("PKCE code verifier", cut.Markup);

        cut.Find("button.disclosure").Click();

        Assert.Contains("PKCE code verifier", cut.Find(".login-card__detail").TextContent);
    }

    private static BunitContext CreateContext(bool configured = true, bool withStoredSession = false)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        if (withStoredSession)
        {
            var module = context.JSInterop.SetupModule("./js/auth.js");
            module.Setup<string?>("getItem", SessionStorageKey).SetResult(SerializeUnexpiredSession());
        }

        context.Services.AddSingleton(sp => new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()));
        context.Services.AddSingleton(sp => new AuthService(
            new HttpClient(new StubIsAdminHandler()),
            new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
            sp.GetRequiredService<NavigationManager>(),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = configured ? "https://example.supabase.co" : string.Empty,
                PublishableKey = configured ? "sb_publishable_123" : string.Empty
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
                User = new AppUser
                {
                    Id = "11111111-1111-1111-1111-111111111111",
                    UserMetadata = { ["user_name"] = "octocat" }
                }
            },
            JsonDefaults.Web);
    }

    private sealed class StubIsAdminHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/rest/v1/rpc/is_admin", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("false")
            });
        }
    }
}
