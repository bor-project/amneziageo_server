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
    /// Returns the ruleset that masquerades and clamps what leaves through the outbounds.
    /// </summary>
    public static string Text(IReadOnlyList<OutboundConfig> outbounds, string uplink)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        var links = Links(outbounds, uplink);
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
        text.Append("\tchain forward {\n");
        text.Append("\t\ttype filter hook forward priority mangle; policy accept;\n");
        foreach (var link in links)
        {
            text.Append("\t\toifname \"").Append(link).Append("\" ").Append(Clamp).Append('\n');
        }

        text.Append("\t}\n}\n");

        return text.ToString();
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
}
