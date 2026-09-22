using System.Net;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the settings of the subscriptions are allowed to hold.
/// </summary>
public static class SubscriptionRules
{
    /// <summary>
    /// The most hours between two readings of a subscription.
    /// </summary>
    public const int MaxUpdateHours = 720;

    /// <summary>
    /// The longest a title is.
    /// </summary>
    public const int MaxTitleLength = 128;

    private static readonly string[] Held = ["api", "assets"];

    private static readonly string[] Served = ["api", "v1"];

    /// <summary>
    /// Returns the first rule the settings break next to the panel they may share a port with.
    /// </summary>
    public static PanelFault? Check(SubscriptionSettings settings, PanelSettings panel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);

        return CheckPort(settings.Port)
            ?? CheckListen(settings.Listen)
            ?? CheckDomains(settings.Domains)
            ?? (settings.Separate ? CheckCertificate(settings.Certificate, settings.CertificateKey) : null)
            ?? CheckPath(settings.Path)
            ?? (settings.Separate ? CheckShared(settings, panel) : CheckServices(settings))
            ?? CheckHours(settings.UpdateHours)
            ?? CheckTitle(settings.Title);
    }

    private static PanelFault? CheckPort(int port) => port is < 1 or > 65535
        ? new PanelFault("bad-port", "the port of the subscriptions is outside 1 to 65535")
        : null;

    private static PanelFault? CheckListen(IReadOnlyList<string> addresses)
    {
        if (addresses.Count > PanelRules.MaxCount)
        {
            return new PanelFault("bad-listen", $"the subscriptions listen on {PanelRules.MaxCount} addresses at most");
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
        if (domains.Count > PanelRules.MaxCount)
        {
            return new PanelFault("bad-domain", $"the subscriptions answer to {PanelRules.MaxCount} names at most");
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

    private static bool Named(string domain) => domain.Length <= PanelRules.MaxDomainLength
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
            return new PanelFault("bad-certificate", "the certificate of the subscriptions takes both a chain and a key");
        }

        return File(chain) && File(key)
            ? null
            : new PanelFault("bad-certificate", "the certificate of the subscriptions takes paths that start with a slash");
    }

    private static bool File(string path) => path.Length <= PanelRules.MaxFileLength
        && path[0] == '/'
        && !path.Contains("..", StringComparison.Ordinal);

    private static PanelFault? CheckPath(string path)
    {
        var shaped = path.Length <= PanelRules.MaxPathLength
            && path.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '.' or '/')
            && !path.Contains("..", StringComparison.Ordinal)
            && path.Trim('/').Length > 0;

        return shaped
            ? null
            : new PanelFault("bad-subscription-path", $"'{path}' is not a path the subscriptions can sit under");
    }

    private static PanelFault? CheckShared(SubscriptionSettings settings, PanelSettings panel)
    {
        if (settings.Port != panel.Port)
        {
            return null;
        }

        var head = Head(settings.Path);
        var taken = Array.Exists(Held, one => string.Equals(one, head, StringComparison.OrdinalIgnoreCase))
            || string.Equals(head, Head(panel.Path), StringComparison.OrdinalIgnoreCase);

        return taken
            ? new PanelFault("subscription-path-taken", $"the panel answers under '/{head}/' on port {settings.Port}")
            : null;
    }

    private static PanelFault? CheckServices(SubscriptionSettings settings)
    {
        var head = Head(settings.Path);

        return Array.Exists(Served, one => string.Equals(one, head, StringComparison.OrdinalIgnoreCase))
            ? new PanelFault("subscription-path-taken", $"the services of the endpoints answer under '/{head}/'")
            : null;
    }

    private static string Head(string path) => path.Trim('/').Split('/')[0];

    private static PanelFault? CheckHours(int hours) => hours is < 1 or > MaxUpdateHours
        ? new PanelFault("bad-subscription-interval", $"a subscription is read again every 1 to {MaxUpdateHours} hours")
        : null;

    private static PanelFault? CheckTitle(string title) =>
        title.Length <= MaxTitleLength && !title.Any(char.IsControl)
            ? null
            : new PanelFault("bad-subscription-title", $"the title is longer than {MaxTitleLength} characters or breaks the line");
}
