namespace AnimeCatalog.Tests;

/// <summary>
/// Every routable page resolves catalog access once, in <c>OnInitializedAsync</c> or
/// <c>OnParametersSetAsync</c>. Signing out fires <c>AuthService.StateChanged</c> but never
/// navigates, so a page without an <c>AuthStateWatcher</c> keeps showing content the visitor can no
/// longer read until the next navigation or a hard refresh.
///
/// The watcher is per-page opt-in rather than a global re-key of the routed view, which means a
/// ninth page can silently forget it. This scan is what makes that safe, in the same spirit as
/// <see cref="SubPathNavigationTests"/>.
/// </summary>
public class AuthStateWatcherCoverageTests
{
    /// <summary>
    /// NotFound renders no catalog data and has no auth branch, so there is nothing for a sign-out
    /// to invalidate. Login is listed separately below, because it needs the watcher but must never
    /// be wired to a load method.
    /// </summary>
    private static readonly string[] PagesWithoutAWatcher = ["NotFound.razor"];

    [Fact]
    public void EveryPageWatchesForAnIdentityChange()
    {
        var pagesRoot = Path.Combine(FindRepositoryRoot(), "src", "AnimeCatalog", "Pages");
        Assert.True(Directory.Exists(pagesRoot), $"Could not locate the pages at '{pagesRoot}'.");

        var missing = EnumeratePages(pagesRoot)
            .Where(file => !PagesWithoutAWatcher.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            .Where(file => !File.ReadAllText(file).Contains("<AuthStateWatcher", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(pagesRoot, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These pages would keep rendering stale content after a sign-out. Add "
            + "<AuthStateWatcher OnIdentityChanged=\"...\" /> outside every conditional branch, or add "
            + $"the page to PagesWithoutAWatcher with a reason.{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void LoginOnlyReRendersOnAnIdentityChange()
    {
        // Login completes the OAuth callback in OnParametersSetAsync behind a _callbackHandled guard.
        // AuthService raises StateChanged from inside HandleOAuthCallbackAsync, before that guard is
        // set, so anything that re-runs the page's load path here re-enters the callback with an auth
        // code Supabase has already consumed. A bare StateHasChanged does not re-run parameters.
        var login = Path.Combine(FindRepositoryRoot(), "src", "AnimeCatalog", "Pages", "Login.razor");

        Assert.Contains(
            "<AuthStateWatcher OnIdentityChanged=\"StateHasChanged\" />",
            File.ReadAllText(login),
            StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumeratePages(string pagesRoot)
        => Directory.EnumerateFiles(pagesRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, pagesRoot))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static bool IsBuildOutput(string path, string pagesRoot)
    {
        var segments = Path.GetRelativePath(pagesRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AnimeCatalog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find AnimeCatalog.slnx above '{AppContext.BaseDirectory}'.");
    }
}
