using System.Security.Claims;
using AnimeCatalog.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace AnimeCatalog.State;

public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal _principal = Anonymous;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(_principal));
    }

    public void SetSession(AuthSession? session, bool isAdmin)
    {
        _principal = session is null ? Anonymous : BuildPrincipal(session, isAdmin);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal BuildPrincipal(AuthSession session, bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.User.Id),
            new(ClaimTypes.Name, session.User.DisplayName)
        };

        if (!string.IsNullOrWhiteSpace(session.User.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, session.User.Email));
        }

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Supabase"));
    }
}
