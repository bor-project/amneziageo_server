using System.Text;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Writes the firewall ruleset that carries what the clients of the endpoints send.
/// </summary>
public static class EndpointRuleset
{
    /// <summary>
    /// The firewall table the endpoint rules live in.
    /// </summary>
    public const string TableName = "amneziageo_in";

    /// <summary>
    /// Returns the ruleset that masquerades the clients and holds them out of the closed ranges.
    /// </summary>
    public static string Text(IReadOnlyList<ServerConfig> configs, string uplink)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(uplink);

        var live = configs.Where(config => config.IsEnabled).ToArray();
        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        Postrouting(text, live, uplink);
        Forward(text, live);
        text.Append("}\n");

        return text.ToString();
    }

    private static void Postrouting(StringBuilder text, IReadOnlyList<ServerConfig> configs, string uplink)
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
        foreach (var config in configs)
        {
            foreach (var range in Networks(config.Address))
            {
                Rule(text, config.Name, range, "accept");
            }

            foreach (var range in Networks(config.Blocked))
            {
                Rule(text, config.Name, range, "reject");
            }
        }

        text.Append("\t}\n");
    }

    private static void Rule(StringBuilder text, string name, AwgAllowedIp range, string verdict)
    {
        text.Append("\t\tiifname \"").Append(name).Append("\" ").Append(Family(range))
            .Append(" daddr ").Append(range).Append(' ').Append(verdict).Append('\n');
    }

    private static string Family(AwgAllowedIp range) => range.IsSix ? "ip6" : "ip";

    private static IReadOnlyList<AwgAllowedIp> Networks(IReadOnlyList<string> ranges) =>
    [
        .. ranges
            .Select(range => AwgAllowedIp.TryParse(range, out var found) ? found.Network() : null)
            .OfType<AwgAllowedIp>()
    ];
}
