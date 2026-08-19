using AnimeCatalog.Services;

namespace AnimeCatalog.Tests;

// Drives AuthStateWatcher without the rest of the auth stack: a page test should not have to build
// an HttpClient, browser storage, Supabase options and the state provider just to render a page that
// watches for a sign-out.
internal sealed class StubAuthStateNotifier : IAuthStateNotifier
{
    public event Action? StateChanged;

    public string? CurrentUserId { get; private set; }

    public bool IsAdmin { get; private set; }

    public StubAuthStateNotifier(string? userId = null, bool isAdmin = false)
    {
        CurrentUserId = userId;
        IsAdmin = isAdmin;
    }

    // Set-then-raise in one call, because every real transition moves the identity and the event
    // together and a test that split them would be testing a state AuthService never produces.
    public void SignInAs(string userId, bool isAdmin = false)
    {
        CurrentUserId = userId;
        IsAdmin = isAdmin;
        StateChanged?.Invoke();
    }

    public void SignOut()
    {
        CurrentUserId = null;
        IsAdmin = false;
        StateChanged?.Invoke();
    }

    // The token-refresh case: AuthService raises the same event with the identity untouched.
    public void RaiseWithoutIdentityChange() => StateChanged?.Invoke();
}
