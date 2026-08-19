using Microsoft.AspNetCore.Components;

namespace AnimeCatalog.Infrastructure;

/// <summary>
/// Builds the login URL that sends the visitor back to the page that refused them.
/// </summary>
public static class LoginLink
{
    public static string ForCurrentPage(NavigationManager navigationManager)
    {
        var relativePath = navigationManager.ToBaseRelativePath(navigationManager.Uri);
        // "." keeps the value non-empty for the home page so it survives the
        // IsNullOrWhiteSpace check in AuthService and still resolves to the base href.
        var returnUrl = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        return $"login?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
