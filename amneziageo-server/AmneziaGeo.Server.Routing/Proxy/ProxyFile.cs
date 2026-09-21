using System.Globalization;
using System.Text;
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

    /// <summary>
    /// The path prefix the websocket of the tunnel comes under.
    /// </summary>
    public const string Prefix = "v1";

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
    /// Returns the whitelist that lets a tunnel reach one port on the loopback of the host.
    /// </summary>
    public static string Whitelist(int target)
    {
        var text = new StringBuilder();
        text.Append("restrictions:\n");
        text.Append("  - name: amneziageo\n");
        text.Append("    match:\n");
        text.Append($"      - !PathPrefix '^{Prefix}$'\n");
        text.Append("    allow:\n");
        text.Append("      - !Tunnel\n");
        text.Append("        protocol:\n");
        text.Append("          - Udp\n");
        text.Append("        port:\n");
        text.Append($"          - {target.ToString(CultureInfo.InvariantCulture)}\n");
        text.Append($"        host: '^{ProxyDefaults.Loopback.Replace(".", "\\.", StringComparison.Ordinal)}$'\n");
        text.Append("        cidr:\n");
        text.Append("          - 127.0.0.0/8\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the arguments a front runs with on its loopback port, as the line the service reads.
    /// </summary>
    public static string Line(int front, string rules) =>
        $"{Variable}=ws://{ProxyDefaults.Loopback}:{front.ToString(CultureInfo.InvariantCulture)} --restrict-config {rules}\n";
}
