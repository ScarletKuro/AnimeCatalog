using System.Text.RegularExpressions;

namespace AnimeCatalog.Tests;

/// <summary>
/// The app is served from a sub-path on GitHub Pages (https://user.github.io/Repository/),
/// where <c>&lt;base href="/Repository/"&gt;</c> only resolves *relative* URLs. An
/// origin-absolute link such as <c>href="/catalog"</c> means "root of this host" and escapes
/// the sub-path, sending visitors to https://user.github.io/catalog.
///
/// That failure is invisible during local development, because the dev server serves the app
/// from the domain root where both forms happen to work. These tests scan the sources so the
/// regression is caught in CI instead of on the deployed site.
/// </summary>
public class SubPathNavigationTests
{
    private static readonly (string Description, Regex Pattern)[] Offenders =
    [
        ("origin-absolute href", new Regex("""[Hh]ref\s*=\s*"/""")),
        ("origin-absolute interpolated path", new Regex("""\$"/""")),
        ("origin-absolute NavigateTo", new Regex("""NavigateTo\(\s*[$@]*"/""")),
    ];

    [Fact]
    public void AppSourcesUseBaseRelativeInternalLinks()
    {
        var appRoot = Path.Combine(FindRepositoryRoot(), "src", "AnimeCatalog");
        Assert.True(Directory.Exists(appRoot), $"Could not locate the app sources at '{appRoot}'.");

        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(appRoot))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (ShouldSkip(line))
                {
                    continue;
                }

                foreach (var (description, pattern) in Offenders)
                {
                    if (pattern.IsMatch(line))
                    {
                        var relativePath = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
                        violations.Add($"{relativePath}:{i + 1}: {description} -> {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Internal links must be base-relative (drop the leading slash) so they survive the "
            + $"/Repository/ base href on GitHub Pages.{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Route templates are matched base-relative by the router and must keep their leading
    /// slash, and fully-qualified external URLs are none of our business.
    /// </summary>
    private static bool ShouldSkip(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("@page", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || line.Contains("://", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string appRoot)
    {
        var patterns = new[] { "*.razor", "*.cs" };

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(appRoot, pattern, SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path, appRoot))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path, string appRoot)
    {
        var relative = Path.GetRelativePath(appRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
