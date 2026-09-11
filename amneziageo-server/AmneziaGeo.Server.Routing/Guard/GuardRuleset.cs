using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace AmneziaGeo.Server.Routing.Guard;

/// <summary>
/// Writes the firewall table that holds the second devices off the ports of the interfaces.
/// </summary>
public static class GuardRuleset
{
    /// <summary>
    /// The firewall table the guard lives in.
    /// </summary>
    public const string TableName = "amneziageo_guard";

    /// <summary>
    /// How long the firewall holds an address off when the panel stops repeating it.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Returns the table holding the addresses off the ports they were caught at.
    /// </summary>
    public static string Text(IReadOnlyList<GuardHold> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        var text = new StringBuilder();
        text.Append("table inet ").Append(TableName).Append('\n');
        text.Append("delete table inet ").Append(TableName).Append('\n');
        text.Append("table inet ").Append(TableName).Append(" {\n");
        Set(text, "cut4", "ipv4_addr", holds.Where(hold => hold.Source.AddressFamily == AddressFamily.InterNetwork));
        Set(text, "cut6", "ipv6_addr", holds.Where(hold => hold.Source.AddressFamily == AddressFamily.InterNetworkV6));
        text.Append("\tchain input {\n");
        text.Append("\t\ttype filter hook input priority -10; policy accept;\n");
        text.Append("\t\tip saddr . udp sport . udp dport @cut4 drop\n");
        text.Append("\t\tip6 saddr . udp sport . udp dport @cut6 drop\n");
        text.Append("\t}\n");
        text.Append("}\n");

        return text.ToString();
    }

    private static void Set(StringBuilder text, string name, string type, IEnumerable<GuardHold> holds)
    {
        var seconds = (int)Timeout.TotalSeconds;
        var items = holds
            .Select(hold => string.Create(
                CultureInfo.InvariantCulture,
                $"{hold.Source.Address} . {hold.Source.Port} . {hold.Port} timeout {seconds}s"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        text.Append("\tset ").Append(name).Append(" {\n");
        text.Append("\t\ttype ").Append(type).Append(" . inet_service . inet_service\n");
        text.Append("\t\tflags timeout\n");
        if (items.Length > 0)
        {
            text.Append("\t\telements = { ").Append(string.Join(", ", items)).Append(" }\n");
        }

        text.Append("\t}\n\n");
    }
}
