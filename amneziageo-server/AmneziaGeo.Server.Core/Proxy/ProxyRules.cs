namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// What the settings of a proxy are allowed to hold.
/// </summary>
public static class ProxyRules
{
    /// <summary>
    /// The longest a name is.
    /// </summary>
    public const int MaxNameLength = 15;

    /// <summary>
    /// The longest a path is.
    /// </summary>
    public const int MaxPathLength = 64;

    /// <summary>
    /// The longest a path to a file is.
    /// </summary>
    public const int MaxFileLength = 256;

    /// <summary>
    /// Returns the first rule the settings break.
    /// </summary>
    public static ProxyFault? Check(ProxyConfig proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return CheckName(proxy.Name)
            ?? CheckPort(proxy.Port)
            ?? CheckPath(proxy.Path)
            ?? CheckCertificate(proxy.Certificate, proxy.CertificateKey);
    }

    /// <summary>
    /// Returns why a proxy cannot be turned on with the certificate it comes down to.
    /// </summary>
    public static ProxyFault? CheckReady(ProxyConfig proxy, string certificate, string key)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        if (!proxy.IsEnabled)
        {
            return null;
        }

        return certificate.Length > 0 && key.Length > 0
            ? null
            : new ProxyFault("no-certificate", "the proxy answers under TLS and neither it nor the panel names a certificate");
    }

    /// <summary>
    /// Returns why the name of a proxy is unusable, or null when it holds.
    /// </summary>
    public static ProxyFault? CheckName(string? name)
    {
        var shaped = !string.IsNullOrWhiteSpace(name)
            && name.Length <= MaxNameLength
            && name.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_');

        return shaped ? null : new ProxyFault("bad-name", $"'{name}' is not a name a proxy takes");
    }

    private static ProxyFault? CheckPort(int port) => port is < 1 or > 65535
        ? new ProxyFault("bad-port", "the port of the proxy is outside 1 to 65535")
        : null;

    private static ProxyFault? CheckPath(string path)
    {
        var trimmed = path.Trim('/');
        var shaped = trimmed.Length is > 0 and <= MaxPathLength
            && !trimmed.Contains("..", StringComparison.Ordinal)
            && trimmed.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '/');

        return shaped ? null : new ProxyFault("bad-path", $"'{path}' is not a path the proxy can serve");
    }

    private static ProxyFault? CheckCertificate(string chain, string key)
    {
        if (chain.Length == 0 && key.Length == 0)
        {
            return null;
        }

        if (chain.Length == 0 || key.Length == 0)
        {
            return new ProxyFault("bad-certificate", "the certificate of the proxy takes both a chain and a key");
        }

        return File(chain) && File(key)
            ? null
            : new ProxyFault("bad-certificate", "the certificate of the proxy takes paths that start with a slash");
    }

    private static bool File(string path) => path.Length <= MaxFileLength
        && path[0] == '/'
        && !path.Contains("..", StringComparison.Ordinal);
}
