using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Routing.Firewall;

/// <summary>
/// Whether the firewall of the host lets a port in from outside, read out of what ufw and nftables show.
/// </summary>
public static partial class PortState
{
    /// <summary>
    /// The firewall lets the port in.
    /// </summary>
    public const string Open = "open";

    /// <summary>
    /// The firewall drops the port.
    /// </summary>
    public const string Closed = "closed";

    /// <summary>
    /// The panel cannot tell.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Reads what <c>ufw status verbose</c> says of a port: the first rule that names it decides, the policy for what comes in otherwise.
    /// </summary>
    public static string OfUfw(string status, string protocol, int port)
    {
        ArgumentNullException.ThrowIfNull(status);

        var lines = status.Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Contains("Status: inactive"))
        {
            return Open;
        }

        if (!lines.Contains("Status: active"))
        {
            return Unknown;
        }

        var policy = Array.Find(lines, line => line.StartsWith("Default:", StringComparison.Ordinal)) ?? string.Empty;
        var rules = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("--", StringComparison.Ordinal))
            {
                rules = true;
                continue;
            }

            if (!rules || line.Length == 0 || line.Contains("(v6)", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = Columns().Split(line);
            if (columns.Length < 2 || columns[1].Contains("OUT", StringComparison.Ordinal)
                || columns[1].Contains("FWD", StringComparison.Ordinal) || !Names(columns[0], protocol, port))
            {
                continue;
            }

            if (columns[1].StartsWith("ALLOW", StringComparison.Ordinal) || columns[1].StartsWith("LIMIT", StringComparison.Ordinal))
            {
                return Open;
            }

            if (columns[1].StartsWith("DENY", StringComparison.Ordinal) || columns[1].StartsWith("REJECT", StringComparison.Ordinal))
            {
                return Closed;
            }
        }

        return policy.Contains("allow (incoming)", StringComparison.Ordinal) ? Open : Closed;
    }

    /// <summary>
    /// Reads what the chains of ufw in nftables say of a port: the chain of the rules ufw was given, the policy of the
    /// input chain otherwise.
    /// </summary>
    public static string OfChains(string input, string rules, string protocol, int port)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var line in rules.Split('\n', StringSplitOptions.TrimEntries))
        {
            var match = Destination().Match(line);
            if (!match.Success
                || !string.Equals(match.Groups[1].Value, protocol, StringComparison.OrdinalIgnoreCase)
                || !match.Groups[2].Value.Trim('{', '}', ' ').Split(',').Any(part => Within(part.Trim(), '-', port)))
            {
                continue;
            }

            var verdict = Verdict(line);
            if (verdict.Length > 0)
            {
                return verdict == "accept" ? Open : Closed;
            }
        }

        var policy = Policy().Match(input);

        return policy.Success && policy.Groups[1].Value != "accept" ? Closed : Open;
    }

    // Tells whether the target of a rule of ufw takes the port.
    private static bool Names(string target, string protocol, int port)
    {
        var on = target.IndexOf(" on ", StringComparison.Ordinal);
        var bare = on >= 0 ? target[..on] : target;
        if (bare == "Anywhere")
        {
            return true;
        }

        var spec = bare[(bare.LastIndexOf(' ') + 1)..];
        var head = spec.Split('/')[0];
        if ((head.Contains('.', StringComparison.Ordinal) || head.Count(one => one == ':') > 1) && IPAddress.TryParse(head, out _))
        {
            return true;
        }

        var slash = spec.IndexOf('/');
        if (slash >= 0 && !string.Equals(spec[(slash + 1)..], protocol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (slash >= 0 ? spec[..slash] : spec).Split(',').Any(part => Within(part, ':', port));
    }

    // Tells whether a port or a range of ports takes the port.
    private static bool Within(string part, char dash, int port)
    {
        var bounds = part.Split(dash);
        if (bounds.Length == 1)
        {
            return int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var single) && single == port;
        }

        return bounds.Length == 2
            && int.TryParse(bounds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var low)
            && int.TryParse(bounds[1], NumberStyles.None, CultureInfo.InvariantCulture, out var high)
            && low <= port && port <= high;
    }

    // Returns what a rule of nftables does with what it takes: accept, drop, reject or nothing.
    private static string Verdict(string line)
    {
        if (line.Contains("limit-accept", StringComparison.Ordinal))
        {
            return "accept";
        }

        var match = Action().Match(line);

        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    [GeneratedRegex(@"\b(tcp|udp) dport (\{[^}]*\}|\S+)")]
    private static partial Regex Destination();

    [GeneratedRegex(@"\s(accept|drop|reject)(\s|$)")]
    private static partial Regex Action();

    [GeneratedRegex(@"policy (\w+);")]
    private static partial Regex Policy();
}
