using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Writes the firewall ruleset that carries what the clients of the endpoints send and what reaches them back.
/// </summary>
public static class EndpointRuleset
{
    /// <summary>
    /// The firewall table the endpoint rules live in.
    /// </summary>
    public const string TableName = "amneziageo_in";

    /// <summary>
    /// Returns the ruleset that masquerades the clients, holds them out of the closed ranges and lets through what the
    /// clients take from the tunnel.
    /// </summary>
    public static string Text(
        IReadOnlyList<ServerConfig> configs,
        IReadOnlyList<TunnelClient> clients,
        string uplink)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(uplink);

        var live = configs.Where(config => config.IsEnabled).ToArray();
        var taken = clients.Where(client => client.IsEnabled).ToArray();
        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        foreach (var config in live)
        {
            Sets(text, config, Open(taken, config));
        }

        Postrouting(text, live, uplink);
        Forward(text, live);
        text.Append("}\n");

        return text.ToString();
    }

    private static void Sets(StringBuilder text, ServerConfig config, IReadOnlyList<AwgAllowedIp> open)
    {
        Set(text, Set4(config.Id), "ipv4_addr", open.Where(range => !range.IsSix));
        Set(text, Set6(config.Id), "ipv6_addr", open.Where(range => range.IsSix));
    }

    private static void Set(StringBuilder text, string name, string type, IEnumerable<AwgAllowedIp> ranges)
    {
        var items = ranges.Select(range => range.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        text.Append("\tset ").Append(name).Append(" {\n");
        text.Append("\t\ttype ").Append(type).Append('\n');
        text.Append("\t\tflags interval\n");
        if (items.Length > 0)
        {
            text.Append("\t\telements = { ").Append(string.Join(", ", items)).Append(" }\n");
        }

        text.Append("\t}\n\n");
    }

    private static void Postrouting(
        StringBuilder text,
        IReadOnlyList<ServerConfig> configs,
        string uplink)
    {
        text.Append("\tchain postrouting {\n");
        text.Append("\t\ttype nat hook postrouting priority srcnat; policy accept;\n");
        foreach (var config in configs.Where(one => one.Nat && uplink.Length > 0))
        {
            foreach (var range in Networks(config.Address))
            {
                text.Append("\t\t").Append(Family(range)).Append(" saddr ").Append(range)
                    .Append(" oifname \"").Append(uplink).Append("\" masquerade\n");
            }
        }

        text.Append("\t}\n\n");
    }

    private static void Forward(StringBuilder text, IReadOnlyList<ServerConfig> configs)
    {
        text.Append("\tchain forward {\n");
        text.Append("\t\ttype filter hook forward priority filter; policy accept;\n");
        text.Append("\t\tct state established,related accept\n");
        foreach (var config in configs)
        {
            Reach(text, config.Name, "ip", Set4(config.Id));
            Reach(text, config.Name, "ip6", Set6(config.Id));
            text.Append("\t\toifname \"").Append(config.Name).Append("\" drop\n");
            foreach (var range in Networks(config.Blocked))
            {
                text.Append("\t\tiifname \"").Append(config.Name).Append("\" ").Append(Family(range))
                    .Append(" daddr ").Append(range).Append(" reject\n");
            }
        }

        text.Append("\t}\n");
    }

    private static void Reach(StringBuilder text, string name, string family, string set)
    {
        text.Append("\t\toifname \"").Append(name).Append("\" ").Append(family).Append(" daddr @").Append(set)
            .Append(" accept\n");
    }

    // The addresses of an endpoint that take a new connection from the network of the tunnel.
    private static IReadOnlyList<AwgAllowedIp> Open(IReadOnlyList<TunnelClient> clients, ServerConfig config) =>
    [
        .. clients
            .Where(client => client.ConfigId == config.Id
                && InboundName.Taken(client.Inbound, config.Inbound) == ClientInbound.Network)
            .SelectMany(client => Points(client.Address))
    ];

    private static string Set4(long configId) => Name(configId, "v4");

    private static string Set6(long configId) => Name(configId, "v6");

    private static string Name(long configId, string family) =>
        string.Create(CultureInfo.InvariantCulture, $"in{configId}{family}");

    private static string Family(AwgAllowedIp range) => range.IsSix ? "ip6" : "ip";

    private static AwgAllowedIp? Range(string text) =>
        AwgAllowedIp.TryParse(text, out var found) ? found : null;

    private static IReadOnlyList<AwgAllowedIp> Points(IReadOnlyList<string> addresses) =>
    [
        .. addresses
            .Select(Range)
            .OfType<AwgAllowedIp>()
            .Select(found => new AwgAllowedIp(found.Address, found.IsSix ? (byte)128 : (byte)32))
    ];

    private static IReadOnlyList<AwgAllowedIp> Networks(IReadOnlyList<string> ranges) =>
    [
        .. ranges
            .Select(range => AwgAllowedIp.TryParse(range, out var found) ? found.Network() : null)
            .OfType<AwgAllowedIp>()
    ];
}
