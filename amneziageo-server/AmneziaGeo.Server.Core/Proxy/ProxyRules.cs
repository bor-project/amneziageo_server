using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Core.Panel;

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
    /// The longest a target is.
    /// </summary>
    public const int MaxTargetLength = 128;

    /// <summary>
    /// The longest a source is.
    /// </summary>
    public const int MaxSourceLength = 64;

    /// <summary>
    /// The most sources a proxy takes.
    /// </summary>
    public const int MaxSources = 16;

    /// <summary>
    /// Returns the first rule the settings break.
    /// </summary>
    public static ProxyFault? Check(ProxyConfig proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return CheckName(proxy.Name)
            ?? CheckKind(proxy.Kind)
            ?? CheckPort(proxy.Port)
            ?? CheckPath(proxy.Kind, proxy.Path)
            ?? CheckTarget(proxy.Kind, proxy.Target)
            ?? CheckSources(proxy.Sources)
            ?? CheckCertificate(proxy.Kind, proxy.Certificate, proxy.CertificateKey);
    }

    /// <summary>
    /// Returns why the kind of a proxy is unusable, or null when it holds.
    /// </summary>
    public static ProxyFault? CheckKind(string? kind) => ProxyKind.Known(kind)
        ? null
        : new ProxyFault("bad-kind", $"'{kind}' is not a kind a proxy takes");

    /// <summary>
    /// Reads a target into a host and a port.
    /// </summary>
    public static bool Target(string text, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var value = (text ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxTargetLength)
        {
            return false;
        }

        var colon = value.LastIndexOf(':');
        if (colon <= 0 || colon == value.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(value[(colon + 1)..], out var read) || read is < 1 or > 65535)
        {
            return false;
        }

        var name = value[..colon].Trim('[', ']');
        var shaped = name.Length > 0
            && !name.Contains("..", StringComparison.Ordinal)
            && name.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '.' or '-' or ':');
        if (!shaped)
        {
            return false;
        }

        host = name;
        port = read;

        return true;
    }

    /// <summary>
    /// Reads a source into the network it stands for.
    /// </summary>
    public static bool Source(string text, out IPNetwork network)
    {
        network = default;
        var value = (text ?? string.Empty).Trim();
        if (value.Length is 0 or > MaxSourceLength)
        {
            return false;
        }

        var slash = value.IndexOf('/');
        var head = slash < 0 ? value : value[..slash];
        if (!IPAddress.TryParse(head, out var address) || !Shaped(head, address))
        {
            return false;
        }

        if (slash < 0)
        {
            network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);

            return true;
        }

        if (!IPNetwork.TryParse(value, out var found) || !found.BaseAddress.Equals(address))
        {
            return false;
        }

        network = found;

        return true;
    }

    /// <summary>
    /// Returns why a proxy cannot be turned on with the certificate it comes down to.
    /// </summary>
    public static ProxyFault? CheckReady(ProxyConfig proxy, string certificate, string key)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        if (!proxy.IsEnabled || !ProxyKind.HasPath(proxy.Kind))
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

    private static ProxyFault? CheckPath(string kind, string path)
    {
        if (!ProxyKind.HasPath(kind))
        {
            return path.Length == 0
                ? null
                : new ProxyFault("bad-path", $"a proxy of the '{kind}' kind serves no path");
        }

        var trimmed = path.Trim('/');
        var shaped = trimmed.Length is > 0 and <= MaxPathLength
            && !trimmed.Contains("..", StringComparison.Ordinal)
            && trimmed.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '/');

        return shaped ? null : new ProxyFault("bad-path", $"'{path}' is not a path the proxy can serve");
    }

    private static ProxyFault? CheckTarget(string kind, string target)
    {
        if (!ProxyKind.HasTarget(kind))
        {
            return target.Length == 0
                ? null
                : new ProxyFault("bad-target", $"a proxy of the '{kind}' kind names no target");
        }

        return Target(target, out _, out _)
            ? null
            : new ProxyFault("bad-target", $"'{target}' is not a host and a port the proxy can pass datagrams to");
    }

    private static bool Shaped(string text, IPAddress address) =>
        address.AddressFamily != AddressFamily.InterNetwork
        || string.Equals(address.ToString(), text, StringComparison.Ordinal);

    private static ProxyFault? CheckSources(IReadOnlyList<string> sources)
    {
        if (sources.Count > MaxSources)
        {
            return new ProxyFault("bad-source", $"a proxy takes {MaxSources} sources at most");
        }

        foreach (var source in sources)
        {
            if (!Source(source, out _))
            {
                return new ProxyFault("bad-source", $"'{source}' is not an address or a network the proxy takes");
            }
        }

        return null;
    }

    private static ProxyFault? CheckCertificate(string kind, string chain, string key)
    {
        if (chain.Length == 0 && key.Length == 0)
        {
            return null;
        }

        if (!ProxyKind.HasPath(kind))
        {
            return new ProxyFault("bad-certificate", $"a proxy of the '{kind}' kind answers under no certificate");
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
