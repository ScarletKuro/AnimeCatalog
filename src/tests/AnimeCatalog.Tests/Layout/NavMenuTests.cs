using System.Net;
using System.Text.Json;
using AnimeCatalog.Infrastructure;
using AnimeCatalog.Layout;
using AnimeCatalog.Models;
using AnimeCatalog.Options;
using AnimeCatalog.Services;
using AnimeCatalog.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AnimeCatalog.Tests.Layout;

public sealed class NavMenuTests
{
    private const string SessionStorageKey = "animeCatalog.auth.session";

    [Fact]
    public void AnonymousVisitor_SeesNoShortcutIntoTheAddPage()
    {
        using var context = CreateContext(isAdmin: false, out _);

        var cut = context.Render<NavMenu>();

        Assert.DoesNotContain("admin/add", cut.Markup);
        Assert.DoesNotContain("+ Add", cut.Markup);
    }

    [Fact]
    public async Task Admin_GetsTheAddShortcutInTheHeader()
    {
        using var context = CreateContext(isAdmin: true, out var authService);

        var cut = context.Render<NavMenu>();
        await authService.InitializeAsync();

        cut.WaitForAssertion(() =>
        {
            var addLink = cut.Find("a.site-nav__link--action");
            Assert.Equal("+ Add", addLink.TextContent);
            Assert.EndsWith("admin/add", addLink.GetAttribute("href"));
        });
    }

    [Fact]
    public async Task AdminLink_DoesNotClaimTheActiveStateOfTheAddPage()
    {
        using var context = CreateContext(isAdmin: true, out var authService);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("admin/add");

        var cut = context.Render<NavMenu>();
        await authService.InitializeAsync();

        // Prefix matching is the NavLink default, so without Match="NavLinkMatch.All" on /admin both
        // admin links would render as active at the same time.
        cut.WaitForAssertion(() =>
        {
            var active = Assert.Single(cut.FindAll("a.active"));
            Assert.Contains("site-nav__link--action", active.ClassList);
        });
    }

    private static BunitContext CreateContext(bool isAdmin, out AuthService authService)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        if (isAdmin)
        {
            // AuthService only asks the is_admin RPC when it has a session to ask about, so the
            // stored session has to be seeded before the admin answer means anything.
            var module = context.JSInterop.SetupModule("./js/auth.js");
            module.Setup<string?>("getItem", SessionStorageKey).SetResult(SerializeUnexpiredSession());
        }

        context.Services.AddSingleton(sp => new AuthService(
            new HttpClient(new StubIsAdminHandler(isAdmin)),
            new BrowserStorageService(sp.GetRequiredService<IJSRuntime>()),
            sp.GetRequiredService<NavigationManager>(),
            Microsoft.Extensions.Options.Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                PublishableKey = "sb_publishable_123"
            }),
            new AppAuthenticationStateProvider()));

        authService = context.Services.GetRequiredService<AuthService>();
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

    private sealed class StubIsAdminHandler : HttpMessageHandler
    {
        private readonly bool _isAdmin;

        public StubIsAdminHandler(bool isAdmin)
        {
            _isAdmin = isAdmin;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/rest/v1/rpc/is_admin", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_isAdmin ? "true" : "false")
            });
        }
    }
}
