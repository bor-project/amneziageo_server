using System.Globalization;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Restricts an endpoint's UDP ingress to the local host.
/// </summary>
public static class UdpPrivacyRuleset
{
    /// <summary>
    /// Names the endpoint's UDP guard table.
    /// </summary>
    public static string Table(string name)
    {
        if (ConfigRules.CheckName(name) is not null)
        {
            throw new ArgumentException("the endpoint name is invalid", nameof(name));
        }

        return "amneziageo_udp_" + name;
    }

    /// <summary>
    /// Blocks UDP ingress outside loopback.
    /// </summary>
    public static string Text(string name, int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var table = Table(name);

        return Clear(name)
            + $"table inet {table} {{\n"
            + "\tchain input {\n"
            + "\t\ttype filter hook input priority filter - 30; policy accept;\n"
            + $"\t\tiifname != \"lo\" udp dport {port.ToString(CultureInfo.InvariantCulture)} counter drop\n"
            + "\t}\n}\n";
    }

    /// <summary>
    /// Removes the endpoint's UDP guard table.
    /// </summary>
    public static string Clear(string name)
    {
        var table = Table(name);

        return $"table inet {table}\ndelete table inet {table}\n";
    }
}
