using AnimeCatalog.Components;
using AnimeCatalog.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeCatalog.Tests.Components;

public sealed class AuthStateWatcherTests
{
    [Fact]
    public void SigningOut_RunsTheCallbackOnce()
    {
        using var context = CreateContext(new StubAuthStateNotifier("owner", isAdmin: true), out var notifier);
        var calls = 0;

        context.Render<AuthStateWatcher>(parameters => parameters
            .Add(p => p.OnIdentityChanged, EventCallback.Factory.Create(this, () => calls++)));

        notifier.SignOut();

        Assert.Equal(1, calls);
    }

    [Fact]
    public void LosingAdmin_RunsTheCallbackEvenThoughTheUserIsUnchanged()
    {
        // The admin pages gate on IsAdmin at render time, so a demotion has to reach them the same
        // way a sign-out does.
        using var context = CreateContext(new StubAuthStateNotifier("owner", isAdmin: true), out var notifier);
        var calls = 0;

        context.Render<AuthStateWatcher>(parameters => parameters
            .Add(p => p.OnIdentityChanged, EventCallback.Factory.Create(this, () => calls++)));

        notifier.SignInAs("owner", isAdmin: false);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void TokenRefresh_RunsNothing()
    {
        // AuthService raises StateChanged from GetAccessTokenAsync on every silent refresh. Reloading
        // there would throw away the search text, status filter and sort the visitor typed into
        // Catalog, for a change that cannot affect what Supabase returns.
        using var context = CreateContext(new StubAuthStateNotifier("owner", isAdmin: true), out var notifier);
        var calls = 0;

        context.Render<AuthStateWatcher>(parameters => parameters
            .Add(p => p.OnIdentityChanged, EventCallback.Factory.Create(this, () => calls++)));

        notifier.RaiseWithoutIdentityChange();

        Assert.Equal(0, calls);
    }

    [Fact]
    public void MountingIntoAnExistingSession_RunsNothing()
    {
        // InitializeAsync restores the session before the first render, so the identity a page mounts
        // into is not a change that page has to react to.
        using var context = CreateContext(new StubAuthStateNotifier("owner", isAdmin: true), out var notifier);
        var calls = 0;

        context.Render<AuthStateWatcher>(parameters => parameters
            .Add(p => p.OnIdentityChanged, EventCallback.Factory.Create(this, () => calls++)));

        Assert.Equal(0, calls);
        notifier.SignInAs("owner", isAdmin: true);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task DisposedWatcher_StopsListening()
    {
        // The subscription outlives the component otherwise: AuthService is app-lifetime in
        // WebAssembly, so a missed unsubscribe leaks every page the visitor ever opened.
        using var context = CreateContext(new StubAuthStateNotifier("owner", isAdmin: true), out var notifier);
        var calls = 0;

        context.Render<AuthStateWatcher>(parameters => parameters
            .Add(p => p.OnIdentityChanged, EventCallback.Factory.Create(this, () => calls++)));

        await context.DisposeComponentsAsync();
        notifier.SignOut();

        Assert.Equal(0, calls);
    }

    private static BunitContext CreateContext(StubAuthStateNotifier stub, out StubAuthStateNotifier notifier)
    {
        notifier = stub;
        var context = new BunitContext();
        context.Services.AddSingleton<IAuthStateNotifier>(stub);
        return context;
    }
}
