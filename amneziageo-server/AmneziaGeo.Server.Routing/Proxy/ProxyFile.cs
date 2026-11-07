using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// The files a websocket front reads: the target it is allowed to reach and the arguments it runs with.
/// </summary>
public static class ProxyFile
{
    /// <summary>
    /// The name of the variable the service takes its arguments from.
    /// </summary>
    public const string Variable = "PROXY_ARGS";

    private const string Head = "proxy-";

    private const string Tail = ".env";

    /// <summary>
    /// Returns the name of the file the allowed target of a front is written to.
    /// </summary>
    public static string Rules(string name) => $"{Head}{name}.yaml";

    /// <summary>
    /// Returns the name of the file the arguments of a front are written to.
    /// </summary>
    public static string Arguments(string name) => $"{Head}{name}{Tail}";

    /// <summary>
    /// Returns the name behind a file of arguments, or null when the file is not one.
    /// </summary>
    public static string? Name(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var bare = Path.GetFileName(file);

        return bare.StartsWith(Head, StringComparison.Ordinal) && bare.EndsWith(Tail, StringComparison.Ordinal) && bare.Length > Head.Length + Tail.Length
            ? bare[Head.Length..^Tail.Length]
            : null;
    }

    /// <summary>
    /// Returns the whitelist that lets a tunnel under a path reach one port on the loopback of the host.
    /// </summary>
    public static string Whitelist(int target, string path, ProxyTargets? targets = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var effective = ProxyTargetRules.TryNormalize(targets, out var normalized) ? normalized : ProxyTargetRules.Denied;
        if (target <= 0 && effective is { Restricted: true })
        {
            return Restricted(path, effective);
        }

        var text = new StringBuilder();
        text.Append("restrictions:\n");
        text.Append("  - name: amneziageo\n");
        text.Append("    match:\n");
        text.Append($"      - !PathPrefix '^{Regex.Escape(path)}$'\n");
        text.Append("    allow:\n");
        text.Append("      - !Tunnel\n");
        text.Append("        protocol:\n");
        text.Append("          - Udp\n");
        if (target > 0)
        {
            text.Append("        port:\n");
            text.Append($"          - {target.ToString(CultureInfo.InvariantCulture)}\n");
            text.Append($"        host: '^{ProxyDefaults.Loopback.Replace(".", "\\.", StringComparison.Ordinal)}$'\n");
            text.Append("        cidr:\n");
            text.Append("          - 127.0.0.0/8\n");
        }
        else
        {
            text.Append("        host: '.*'\n");
            text.Append("        cidr:\n");
            text.Append("          - 0.0.0.0/0\n");
            text.Append("          - ::/0\n");
        }

        return text.ToString();
    }

    private static string Restricted(string path, ProxyTargets targets)
    {
        var text = new StringBuilder();
        text.Append("restrictions:\n  - name: amneziageo\n    match:\n");
        text.Append($"      - !PathPrefix '^{Regex.Escape(path)}$'\n");
        if (!ProxyTargetRules.TryNormalize(targets, out var policy))
        {
            text.Append("    allow: []\n");
            return text.ToString();
        }

        text.Append("    allow:\n");
        foreach (var destination in policy!.Destinations)
        {
            text.Append("      - !Tunnel\n        protocol:\n          - Udp\n        port:\n");
            text.Append($"          - {destination.Port.ToString(CultureInfo.InvariantCulture)}\n");
            if (IPAddress.TryParse(destination.Host, out var address))
            {
                var prefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                text.Append($"        host: '^$'\n        cidr:\n          - '{destination.Host}/{prefix}'\n");
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    text.Append($"          - '{address.MapToIPv6()}/128'\n");
                }
            }
            else
            {
                text.Append($"        host: '{ProxyTargetRules.HostPattern(destination.Host)}'\n");
                text.Append("        cidr: []\n");
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Returns the arguments a front runs with on its loopback port, as the line the service reads.
    /// </summary>
    public static string Line(int front, string rules) =>
        $"{Variable}=ws://{ProxyDefaults.Loopback}:{front.ToString(CultureInfo.InvariantCulture)} --restrict-config {rules}\n";
}
