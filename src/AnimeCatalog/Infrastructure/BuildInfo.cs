namespace AnimeCatalog.Infrastructure;

using System.Reflection;

public static class BuildInfo
{
    public const string SourceRepositoryUrl = "https://github.com/ScarletKuro/AnimeCatalog";

    public static string? FullCommitSha => ResolveCommitSha();

    public static string ShortCommitSha =>
        FullCommitSha is { Length: >= 7 } commitSha
            ? commitSha[..7]
            : "local";

    public static string CommitUrl =>
        FullCommitSha is { } commitSha
            ? $"{SourceRepositoryUrl}/commit/{commitSha}"
            : SourceRepositoryUrl;

    private static string? ResolveCommitSha()
    {
        var version = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var metadataStart = version.LastIndexOf('+');
        if (metadataStart < 0 || metadataStart == version.Length - 1)
        {
            return null;
        }

        var candidate = version[(metadataStart + 1)..].Trim();
        return candidate.Length >= 7 && candidate.All(IsHexCharacter)
            ? candidate
            : null;
    }

    private static bool IsHexCharacter(char value) =>
        value is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
}
