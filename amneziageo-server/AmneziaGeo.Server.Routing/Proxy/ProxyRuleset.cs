using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Writes the firewall ruleset that holds a proxy to the sources it names.
/// </summary>
public static class ProxyRuleset
{
    /// <summary>
    /// The firewall table the rules of the proxies live in.
    /// </summary>
    public const string TableName = "amneziageo_proxy";

    /// <summary>
    /// Returns the ruleset that drops what reaches a proxy from a source it does not name.
    /// </summary>
    public static string Text(IReadOnlyList<ProxyConfig> proxies)
    {
        ArgumentNullException.ThrowIfNull(proxies);

        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        text.Append("\tchain input {\n");
        text.Append("\t\ttype filter hook input priority filter; policy accept;\n");
        foreach (var proxy in proxies.Where(one => one.IsEnabled && one.Sources.Count > 0))
        {
            Rules(text, proxy);
        }

        text.Append("\t}\n");
        text.Append("}\n");

        return text.ToString();
    }

    private static void Rules(StringBuilder text, ProxyConfig proxy)
    {
        var networks = Networks(proxy.Sources);
        if (networks.Count == 0)
        {
            return;
        }

        var head = (ProxyKind.HasTarget(proxy.Kind) ? "udp" : "tcp")
            + " dport "
            + proxy.Port.ToString(CultureInfo.InvariantCulture);
        Rule(text, head, "ip", "ipv4", Family(networks, AddressFamily.InterNetwork));
        Rule(text, head, "ip6", "ipv6", Family(networks, AddressFamily.InterNetworkV6));
    }

    private static void Rule(StringBuilder text, string head, string family, string protocol, string[] networks)
    {
        text.Append("\t\t").Append(head);
        if (networks.Length == 0)
        {
            text.Append(" meta nfproto ").Append(protocol).Append(" drop\n");

            return;
        }

        text.Append(' ').Append(family).Append(" saddr != { ").Append(string.Join(", ", networks)).Append(" } drop\n");
    }

    private static string[] Family(IReadOnlyList<IPNetwork> networks, AddressFamily family) =>
    [
        .. networks.Where(one => one.BaseAddress.AddressFamily == family).Select(one => one.ToString())
    ];

    private static IReadOnlyList<IPNetwork> Networks(IReadOnlyList<string> sources) =>
    [
        .. sources
            .Select(source => ProxyRules.Source(source, out var found) ? found : default(IPNetwork?))
            .OfType<IPNetwork>()
    ];
}
