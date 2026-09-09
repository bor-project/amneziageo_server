using System.Net;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the settings of the panel are allowed to hold.
/// </summary>
public static class PanelRules
{
    /// <summary>
    /// The longest a domain is.
    /// </summary>
    public const int MaxDomainLength = 253;

    /// <summary>
    /// The longest a path is.
    /// </summary>
    public const int MaxPathLength = 64;

    /// <summary>
    /// The longest a path to a file is.
    /// </summary>
    public const int MaxFileLength = 256;

    /// <summary>
    /// The most addresses and names a setting holds.
    /// </summary>
    public const int MaxCount = 16;

    /// <summary>
    /// Returns the first rule the settings break.
    /// </summary>
    public static PanelFault? Check(PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return CheckPort(settings.Port)
            ?? CheckListen(settings.Listen)
            ?? CheckDomains(settings.Domains)
            ?? CheckCertificate(settings.Certificate, settings.CertificateKey)
            ?? CheckPath(settings.Path)
            ?? CheckLanguage(settings.Language);
    }

    private static PanelFault? CheckPort(int port) => port is < 1 or > 65535
        ? new PanelFault("bad-port", "the port of the panel is outside 1 to 65535")
        : null;

    private static PanelFault? CheckListen(IReadOnlyList<string> addresses)
    {
        if (addresses.Count > MaxCount)
        {
            return new PanelFault("bad-listen", $"the panel listens on {MaxCount} addresses at most");
        }

        foreach (var address in addresses)
        {
            if (!IPAddress.TryParse(address, out _))
            {
                return new PanelFault("bad-listen", $"'{address}' is not an address to listen on");
            }
        }

        return null;
    }

    private static PanelFault? CheckDomains(IReadOnlyList<string> domains)
    {
        if (domains.Count > MaxCount)
        {
            return new PanelFault("bad-domain", $"the panel answers to {MaxCount} names at most");
        }

        foreach (var domain in domains)
        {
            if (!Named(domain))
            {
                return new PanelFault("bad-domain", $"'{domain}' is not a domain name");
            }
        }

        return null;
    }

    private static bool Named(string domain) => domain.Length <= MaxDomainLength
        && domain[0] != '.'
        && domain[^1] != '.'
        && !domain.Contains("..", StringComparison.Ordinal)
        && domain.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '.');

    private static PanelFault? CheckCertificate(string chain, string key)
    {
        if (chain.Length == 0 && key.Length == 0)
        {
            return null;
        }

        if (chain.Length == 0 || key.Length == 0)
        {
            return new PanelFault("bad-certificate", "the certificate of the panel takes both a chain and a key");
        }

        return File(chain) && File(key)
            ? null
            : new PanelFault("bad-certificate", "the certificate of the panel takes paths that start with a slash");
    }

    private static bool File(string path) => path.Length <= MaxFileLength
        && path[0] == '/'
        && !path.Contains("..", StringComparison.Ordinal);

    private static PanelFault? CheckPath(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        var shaped = path.Length <= MaxPathLength
            && path.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '.' or '/')
            && !path.Contains("..", StringComparison.Ordinal)
            && path.Trim('/').Length > 0;

        return shaped ? null : new PanelFault("bad-path", $"'{path}' is not a path the panel can sit under");
    }

    private static PanelFault? CheckLanguage(string language) =>
        Array.IndexOf(PanelDefaults.Languages, language) < 0
            ? new PanelFault("bad-language", $"'{language}' is not a language the panel opens in")
            : null;
}
