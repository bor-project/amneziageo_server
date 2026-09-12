using System.Globalization;
using System.Text;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// Writes the firewall table that takes the open ports where the host has no ufw.
/// </summary>
public static class OpenRuleset
{
    /// <summary>
    /// The firewall table the open ports live in.
    /// </summary>
    public const string TableName = "amneziageo_open";

    /// <summary>
    /// Returns the table that takes the ports of the plan and passes what travels through its interfaces.
    /// </summary>
    public static string Text(FirewallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        text.Append("\tchain input {\n");
        text.Append("\t\ttype filter hook input priority filter - 20; policy accept;\n");
        foreach (var port in plan.Ports)
        {
            text.Append("\t\t").Append(port.Protocol).Append(" dport ").Append(Number(port.Port))
                .Append(" accept\n");
        }

        text.Append("\t}\n\n");
        text.Append("\tchain forward {\n");
        text.Append("\t\ttype filter hook forward priority filter - 20; policy accept;\n");
        foreach (var name in plan.Interfaces)
        {
            text.Append("\t\tiifname \"").Append(name).Append("\" accept\n");
            text.Append("\t\toifname \"").Append(name).Append("\" accept\n");
        }

        text.Append("\t}\n");
        text.Append("}\n");

        return text.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
