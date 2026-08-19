namespace AnimeCatalog.Services;

// The read side of AuthService that a page needs in order to react to a sign-in or a sign-out.
// Narrow for the same reason as IAccessTokenProvider and IAdminAuthorizationService: AuthStateWatcher
// ends up on nearly every page, and a page test should not have to build the whole auth stack just
// to render one.
public interface IAuthStateNotifier
{
    event Action? StateChanged;

    // Null when nobody is signed in. Identity, not token: a refresh keeps this stable.
    string? CurrentUserId { get; }

    bool IsAdmin { get; }
}
