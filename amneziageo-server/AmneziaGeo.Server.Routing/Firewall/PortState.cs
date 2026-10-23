using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Device;

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
    /// input chain otherwise, unknown from a rule whose port nftables shows only as a match of iptables.
    /// </summary>
    public static string OfChains(string input, string rules, string protocol, int port)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var line in rules.Split('\n', StringSplitOptions.TrimEntries))
        {
            var verdict = Verdict(line);
            if (verdict.Length == 0)
            {
                continue;
            }

            var match = Destination().Match(line);
            if (!match.Success && line.Contains("xt match", StringComparison.Ordinal))
            {
                return Unknown;
            }

            if (match.Success
                && string.Equals(match.Groups[1].Value, protocol, StringComparison.OrdinalIgnoreCase)
                && match.Groups[2].Value.Trim('{', '}', ' ').Split(',').Any(part => Within(part.Trim(), '-', port)))
            {
                return verdict == "accept" ? Open : Closed;
            }
        }

        var policy = Policy().Match(input);

        return policy.Success && policy.Groups[1].Value != "accept" ? Closed : Open;
    }

    /// <summary>
    /// Reads what <c>ufw status verbose</c> says of a port for what comes in on one interface: a rule for another
    /// interface or from some addresses only does not decide.
    /// </summary>
    public static string OfUfw(string status, string protocol, int port, string inbound) =>
        OfUfw(status, protocol, port, inbound, []);

    /// <summary>
    /// Reads what <c>ufw status verbose</c> says of a port for what comes in on one interface that carries the given
    /// addresses: a rule for another interface does not decide, and a rule from some addresses only decides when
    /// they hold every subnet of the interface in their family.
    /// </summary>
    public static string OfUfw(string status, string protocol, int port, string inbound, IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(addresses);

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
            if (columns.Length < 3 || columns[1].Contains("OUT", StringComparison.Ordinal)
                || columns[1].Contains("FWD", StringComparison.Ordinal) || !Lets(columns[2], addresses)
                || !Takes(OnInterface(columns[0]), inbound) || !Names(columns[0], protocol, port))
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
    /// Reads what the chains of ufw in nftables say of a port for what comes in on one interface: a rule for another
    /// interface or for some addresses only does not decide, a rule for the interface without a port does.
    /// </summary>
    public static string OfChains(string input, string rules, string protocol, int port, string inbound) =>
        OfChains(input, rules, protocol, port, inbound, []);

    /// <summary>
    /// Reads what the chains of ufw in nftables say of a port for what comes in on one interface that carries the
    /// given addresses: a rule for another interface does not decide, a rule from some addresses only decides when
    /// they hold every subnet of the interface in their family, a rule for the interface without a port does.
    /// </summary>
    public static string OfChains(
        string input,
        string rules,
        string protocol,
        int port,
        string inbound,
        IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(addresses);

        foreach (var line in rules.Split('\n', StringSplitOptions.TrimEntries))
        {
            var verdict = Verdict(line);
            if (verdict.Length == 0 || Destined().IsMatch(line) || line.Contains("oifname", StringComparison.Ordinal))
            {
                continue;
            }

            var sources = Sourced().Match(line);
            if (sources.Success
                && (sources.Groups[2].Length > 0 || !Covers(Ranges(sources.Groups[3].Value), addresses, sources.Groups[1].Length > 0)))
            {
                continue;
            }

            var named = Inbound().Match(line);
            if (named.Success && !Takes(named.Groups[1].Value + named.Groups[2].Value, inbound))
            {
                continue;
            }

            var match = Destination().Match(line);
            if (match.Success)
            {
                if (string.Equals(match.Groups[1].Value, protocol, StringComparison.OrdinalIgnoreCase)
                    && match.Groups[2].Value.Trim('{', '}', ' ').Split(',').Any(part => Within(part.Trim(), '-', port)))
                {
                    return verdict == "accept" ? Open : Closed;
                }

                continue;
            }

            if (line.Contains("xt match", StringComparison.Ordinal))
            {
                return Unknown;
            }

            var carried = Carried().Match(line);
            if (named.Success && !line.Contains("ct state", StringComparison.Ordinal)
                && (!carried.Success || string.Equals(carried.Groups[1].Value, protocol, StringComparison.OrdinalIgnoreCase)))
            {
                return verdict == "accept" ? Open : Closed;
            }
        }

        var policy = Policy().Match(input);

        return policy.Success && policy.Groups[1].Value != "accept" ? Closed : Open;
    }

    // Returns the interface a rule of ufw names after " on ", empty when it names none.
    private static string OnInterface(string target)
    {
        var on = target.IndexOf(" on ", StringComparison.Ordinal);

        return on >= 0 ? target[(on + 4)..].Trim() : string.Empty;
    }

    // Tells whether the interfaces a rule names take one interface: none named takes every one, a name ending in + or *
    // takes the ones it starts.
    private static bool Takes(string named, string inbound)
    {
        if (named.Length == 0)
        {
            return true;
        }

        foreach (var one in named.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var name = one.Trim('"');
            var wild = name.EndsWith('+') || name.EndsWith('*');
            if (wild ? inbound.StartsWith(name[..^1], StringComparison.Ordinal) : name == inbound)
            {
                return true;
            }
        }

        return false;
    }

    // Tells whether the addresses a rule of ufw lets in take every client of an interface: everyone, or a network that
    // holds every subnet of the interface in its family.
    private static bool Lets(string from, IReadOnlyList<string> addresses)
    {
        var source = from.Split(' ', 2)[0];

        return source == "Anywhere"
            || (AwgAllowedIp.TryParse(source, out var range) && Covers([range], addresses, range.IsSix));
    }

    // Tells whether some sources hold every subnet an interface carries in a family: each subnet lies inside one of them.
    private static bool Covers(IReadOnlyList<AwgAllowedIp> sources, IReadOnlyList<string> addresses, bool six)
    {
        var subnets = new List<AwgAllowedIp>();
        foreach (var text in addresses)
        {
            if (AwgAllowedIp.TryParse(text, out var carried) && carried.IsSix == six)
            {
                subnets.Add(carried.Network());
            }
        }

        return subnets.Count > 0
            && subnets.All(subnet => sources.Any(source => source.Cidr <= subnet.Cidr && ClientPool.Inside(source, subnet.Address)));
    }

    // Returns the ranges a rule of nftables names, one or a set in braces.
    private static List<AwgAllowedIp> Ranges(string text)
    {
        var ranges = new List<AwgAllowedIp>();
        foreach (var part in text.Trim('{', '}', ' ').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (AwgAllowedIp.TryParse(part, out var range))
            {
                ranges.Add(range);
            }
        }

        return ranges;
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

    [GeneratedRegex(@"\biifname (?:""([^""]+)""|\{([^}]*)\})")]
    private static partial Regex Inbound();

    [GeneratedRegex(@"\bip6? daddr\b")]
    private static partial Regex Destined();

    [GeneratedRegex(@"\bip(6?) saddr (!= )?(\{[^}]*\}|\S+)")]
    private static partial Regex Sourced();

    [GeneratedRegex(@"\bmeta l4proto (\w+)")]
    private static partial Regex Carried();
}
