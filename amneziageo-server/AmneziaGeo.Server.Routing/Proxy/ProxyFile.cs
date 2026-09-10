using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// The files a proxy reads: the targets it is allowed to reach and the arguments it runs with.
/// </summary>
public static class ProxyFile
{
    /// <summary>
    /// The name of the variable the service takes its arguments from.
    /// </summary>
    public const string Variable = "PROXY_ARGS";

    /// <summary>
    /// Returns the name of the file the allowed targets of a proxy are written to.
    /// </summary>
    public static string Rules(string name) => $"proxy-{name}.yaml";

    /// <summary>
    /// Returns the name of the file the arguments of a proxy are written to.
    /// </summary>
    public static string Arguments(string name) => $"proxy-{name}.env";

    /// <summary>
    /// Returns the whitelist that lets a tunnel reach the named ports on the loopback of the host.
    /// </summary>
    public static string Whitelist(string path, IReadOnlyList<int> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        var prefix = (path ?? string.Empty).Trim('/');
        if (prefix.Length == 0 || ports.Count == 0)
        {
            return "restrictions: []\n";
        }

        var text = new StringBuilder();
        text.Append("restrictions:\n");
        text.Append("  - name: amneziageo\n");
        text.Append("    match:\n");
        text.Append($"      - !PathPrefix '^{prefix}$'\n");
        text.Append("    allow:\n");
        text.Append("      - !Tunnel\n");
        text.Append("        protocol:\n");
        text.Append("          - Udp\n");
        text.Append("        port:\n");
        foreach (var port in ports.Distinct().Order())
        {
            text.Append($"          - {port.ToString(CultureInfo.InvariantCulture)}\n");
        }

        text.Append("        host: '^127\\.0\\.0\\.1$'\n");
        text.Append("        cidr:\n");
        text.Append("          - 127.0.0.0/8\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the arguments a proxy runs with, as the line the service reads.
    /// </summary>
    public static string Line(ProxyConfig proxy, string certificate, string key, string rules)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        if (ProxyKind.HasTarget(proxy.Kind))
        {
            return Relay(proxy);
        }

        var tls = certificate.Length > 0 && key.Length > 0;
        var scheme = tls ? "wss" : "ws";
        var text = new StringBuilder();
        text.Append($"{Variable}={scheme}://0.0.0.0:{proxy.Port.ToString(CultureInfo.InvariantCulture)}");
        text.Append($" --restrict-config {rules}");
        if (tls)
        {
            text.Append($" --tls-certificate {certificate}");
            text.Append($" --tls-private-key {key}");
        }

        text.Append('\n');

        return text.ToString();
    }

    private static string Relay(ProxyConfig proxy)
    {
        var port = proxy.Port.ToString(CultureInfo.InvariantCulture);
        var idle = ProxyDefaults.Idle.ToString(CultureInfo.InvariantCulture);

        return $"{Variable}=--listen 0.0.0.0:{port} --target {proxy.Target} --idle {idle}\n";
    }
}
