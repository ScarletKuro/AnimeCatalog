using System.Security.Cryptography;
using System.Text;

namespace AnimeCatalog.Infrastructure;

public static class PkceUtility
{
    public static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    public static string CreateCodeChallenge(string verifier)
    {
        var verifierBytes = Encoding.UTF8.GetBytes(verifier);
        var hash = SHA256.HashData(verifierBytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
