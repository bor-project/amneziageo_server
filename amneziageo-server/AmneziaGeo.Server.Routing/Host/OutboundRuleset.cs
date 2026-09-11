using System.Text;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Writes the firewall ruleset that lets traffic out through the outbounds.
/// </summary>
public static class OutboundRuleset
{
    /// <summary>
    /// The firewall table the outbound rules live in.
    /// </summary>
    public const string TableName = "amneziageo_out";

    /// <summary>
    /// The rule that holds a segment down to what the path carries.
    /// </summary>
    public const string Clamp = "tcp flags syn / syn,rst tcp option maxseg size set rt mtu";

    /// <summary>
    /// The private ranges of the first family a tunnel closes to the clients.
    /// </summary>
    public static readonly string[] Private4 =
        ["10.0.0.0/8", "100.64.0.0/10", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16"];

    /// <summary>
    /// The private ranges of the second family a tunnel closes to the clients.
    /// </summary>
    public static readonly string[] Private6 = ["fc00::/7", "fe80::/10"];

    /// <summary>
    /// Returns the ruleset that masquerades, clamps and filters what passes through the outbounds.
    /// </summary>
    public static string Text(IReadOnlyList<OutboundConfig> outbounds, string uplink)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        var links = Links(outbounds, uplink);
        var tunnels = Tunnels(outbounds);
        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        text.Append("\tchain postrouting {\n");
        text.Append("\t\ttype nat hook postrouting priority srcnat; policy accept;\n");
        foreach (var link in links)
        {
            text.Append("\t\toifname \"").Append(link).Append("\" masquerade\n");
        }

        text.Append("\t}\n\n");
        text.Append("\tchain input {\n");
        text.Append("\t\ttype filter hook input priority filter; policy accept;\n");
        Answers(text, tunnels);
        text.Append("\t}\n\n");
        text.Append("\tchain forward {\n");
        text.Append("\t\ttype filter hook forward priority mangle; policy accept;\n");
        foreach (var link in links)
        {
            text.Append("\t\toifname \"").Append(link).Append("\" ").Append(Clamp).Append('\n');
        }

        if (!string.IsNullOrEmpty(uplink))
        {
            text.Append("\t\tiifname \"").Append(uplink).Append("\" oifname \"").Append(uplink).Append("\" drop\n");
        }

        foreach (var outbound in outbounds.Where(one => one.IsEnabled && one.ClosePrivate && OutboundKind.HasLink(one.Kind)))
        {
            Close(text, outbound.Name);
        }

        Answers(text, tunnels);
        text.Append("\t}\n}\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the interfaces of the tunnels that are on.
    /// </summary>
    public static IReadOnlyList<string> Tunnels(IReadOnlyList<OutboundConfig> outbounds)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        return
        [
            .. outbounds
                .Where(one => one.IsEnabled && OutboundKind.HasLink(one.Kind))
                .Select(one => one.Name)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Returns the interfaces traffic leaves the host through.
    /// </summary>
    public static IReadOnlyList<string> Links(IReadOnlyList<OutboundConfig> outbounds, string uplink)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        var links = new List<string>();
        foreach (var outbound in outbounds.Where(one => one.IsEnabled))
        {
            var link = OutboundKind.HasLink(outbound.Kind) ? outbound.Name : uplink;
            if (link.Length > 0 && !links.Contains(link))
            {
                links.Add(link);
            }
        }

        return links;
    }

    private static void Answers(StringBuilder text, IReadOnlyList<string> tunnels)
    {
        if (tunnels.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", tunnels.Select(name => $"\"{name}\""));
        text.Append("\t\tiifname { ").Append(names).Append(" } ct state established,related accept\n");
        text.Append("\t\tiifname { ").Append(names).Append(" } drop\n");
    }

    private static void Close(StringBuilder text, string name)
    {
        text.Append("\t\toifname \"").Append(name).Append("\" ct state new ip daddr { ")
            .Append(string.Join(", ", Private4)).Append(" } reject\n");
        text.Append("\t\toifname \"").Append(name).Append("\" ct state new ip6 daddr { ")
            .Append(string.Join(", ", Private6)).Append(" } reject\n");
    }
}
