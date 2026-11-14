using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Defines anonymous transport credentials.
/// </summary>
public static class ProxyAccess
{
    public const string AnonymousCredential = "f4514728-b141-4b09-a1fe-4a5a5f83cf51";

    /// <summary>
    /// Adds the anonymous protocol credential.
    /// </summary>
    public static IReadOnlyList<string> Credentials(IReadOnlyList<string> credentials, bool anonymous) =>
        [.. (anonymous ? credentials.Append(AnonymousCredential) : credentials).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Identifies an HTTP transport credential.
    /// </summary>
    public static string HttpIdentity(string credential) => credential.Length == 36 && Guid.TryParseExact(credential, "D", out var id) && id != Guid.Empty
        ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("amneziageo.http-get.id.v2\0" + id.ToString("D")))).ToLowerInvariant() : string.Empty;

    /// <summary>
    /// Checks the HTTP authorization header.
    /// </summary>
    public static bool AllowsHttp(bool anonymous, IReadOnlyList<string> bearerIds, string authorization, string identity)
    {
        if (anonymous)
        {
            return true;
        }

        if (authorization.Length == 0)
        {
            return !bearerIds.Contains(identity, StringComparer.Ordinal);
        }

        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length > 263)
        {
            return false;
        }

        var expected = HttpIdentity(authorization[7..]);
        return expected.Length > 0 && string.Equals(expected, identity, StringComparison.Ordinal);
    }
}
