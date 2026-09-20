using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Geo;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Shapes the settings of a routing rule have to take.
/// </summary>
public static class RouteRules
{
    /// <summary>
    /// The longest name a rule takes.
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// The longest domain a rule matches by.
    /// </summary>
    public const int MaxDomainLength = 253;

    /// <summary>
    /// How many conditions of one kind a rule carries.
    /// </summary>
    public const int MaxConditions = 64;

    /// <summary>
    /// How many targets one of the basic lists carries.
    /// </summary>
    public const int MaxBasic = 1024;

    /// <summary>
    /// Returns why the settings of a rule are unusable, or null when they hold.
    /// </summary>
    public static RouteFault? Check(RouteRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return CheckName(rule.Name)
            ?? CheckAction(rule)
            ?? CheckProtocol(rule.Protocol)
            ?? CheckTargets(rule.Targets, MaxConditions)
            ?? CheckSources(rule.Sources)
            ?? CheckClients(rule.Clients)
            ?? CheckInbounds(rule.Inbounds)
            ?? CheckPorts(rule.Ports, "bad-port", "ports")
            ?? CheckPorts(rule.SourcePorts, "bad-source-port", "source ports");
    }

    /// <summary>
    /// Returns why the basic lists are unusable, or null when they hold.
    /// </summary>
    public static RouteFault? CheckBasic(RouteBasic basic)
    {
        ArgumentNullException.ThrowIfNull(basic);

        return CheckTargets(basic.Direct, MaxBasic) ?? CheckTargets(basic.Block, MaxBasic);
    }

    /// <summary>
    /// Returns why the name of a rule is unusable, or null when it holds.
    /// </summary>
    public static RouteFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return new RouteFault("bad-rule-name", $"the name is empty or longer than {MaxNameLength} characters");
        }

        return name.Any(char.IsControl)
            ? new RouteFault("bad-rule-name", "the name carries a character that is not printable")
            : null;
    }

    /// <summary>
    /// Reads a target written as a geo key, a domain or an address range.
    /// </summary>
    public static GeoRule? Target(string? value)
    {
        var body = (value ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return null;
        }

        if (Head(body, "geoip:") is { } country)
        {
            return Key(country) ? new GeoRule(GeoRuleKind.GeoIp, country) : null;
        }

        if (Head(body, "geosite:") is { } category)
        {
            return Key(category) ? new GeoRule(GeoRuleKind.GeoSite, category) : null;
        }

        if (Head(body, "domain:") is { } name)
        {
            return name.Length > 0 && Domain(name) ? new GeoRule(GeoRuleKind.Domain, name.ToLowerInvariant()) : null;
        }

        if (Head(body, "keyword:") is { } word)
        {
            return Word(word) ? new GeoRule(GeoRuleKind.Keyword, word.ToLowerInvariant()) : null;
        }

        if (AwgAllowedIp.TryParse(body, out var range))
        {
            return new GeoRule(GeoRuleKind.Cidr, range.ToString());
        }

        return Domain(body) ? new GeoRule(GeoRuleKind.Domain, body.ToLowerInvariant()) : null;
    }

    /// <summary>
    /// Reads a range of ports written as one number or as two through a dash.
    /// </summary>
    public static bool Ports(string? value, out int low, out int high)
    {
        low = 0;
        high = 0;
        var body = (value ?? string.Empty).Trim();
        var dash = body.IndexOf('-', StringComparison.Ordinal);
        var head = dash < 0 ? body : body[..dash].Trim();
        var tail = dash < 0 ? body : body[(dash + 1)..].Trim();
        if (!int.TryParse(head, out low) || !int.TryParse(tail, out high))
        {
            return false;
        }

        return low is > 0 and <= 65535 && high is > 0 and <= 65535 && low <= high;
    }

    /// <summary>
    /// Reads an address range the traffic comes from.
    /// </summary>
    public static bool Source(string? value, out AwgAllowedIp range) => AwgAllowedIp.TryParse(value, out range);

    private static RouteFault? CheckAction(RouteRule rule)
    {
        if (!RouteAction.Known(rule.Action))
        {
            return new RouteFault("bad-action", $"'{rule.Action}' is not an action a rule takes");
        }

        return rule.Action == RouteAction.Out && rule.Outbound.Trim().Length == 0
            ? new RouteFault("no-outbound", "the rule names no outbound to send the traffic through")
            : null;
    }

    private static RouteFault? CheckProtocol(string? protocol) =>
        RouteProtocol.Known(protocol)
            ? null
            : new RouteFault("bad-protocol", $"'{protocol}' is not a protocol a rule matches");

    private static RouteFault? CheckTargets(IReadOnlyList<string> targets, int most)
    {
        if (targets.Count > most)
        {
            return Many(most, "targets");
        }

        foreach (var target in targets)
        {
            if (Target(target) is null)
            {
                return new RouteFault(
                    "bad-target",
                    $"'{target}' is not a geo key, a domain, a keyword or an address range");
            }
        }

        return null;
    }

    private static RouteFault? CheckSources(IReadOnlyList<string> sources)
    {
        if (sources.Count > MaxConditions)
        {
            return Many("client addresses");
        }

        foreach (var source in sources)
        {
            if (!Source(source, out _))
            {
                return new RouteFault("bad-source", $"'{source}' is not a client address or range");
            }
        }

        return null;
    }

    private static RouteFault? CheckClients(IReadOnlyList<string> clients)
    {
        if (clients.Count > MaxConditions)
        {
            return Many("clients");
        }

        foreach (var client in clients)
        {
            if (ClientRules.CheckName(client) is not null)
            {
                return new RouteFault("bad-rule-client", $"'{client}' is not a name a client takes");
            }
        }

        return null;
    }

    private static RouteFault? CheckInbounds(IReadOnlyList<string> inbounds)
    {
        if (inbounds.Count > MaxConditions)
        {
            return Many("interfaces");
        }

        foreach (var inbound in inbounds)
        {
            if (ConfigRules.CheckName(inbound) is not null)
            {
                return new RouteFault("bad-rule-inbound", $"'{inbound}' is not a name an interface takes");
            }
        }

        return null;
    }

    private static RouteFault? CheckPorts(IReadOnlyList<string> ports, string code, string what)
    {
        if (ports.Count > MaxConditions)
        {
            return Many(what);
        }

        foreach (var port in ports)
        {
            if (!Ports(port, out _, out _))
            {
                return new RouteFault(code, $"'{port}' is not a port or a range of ports");
            }
        }

        return null;
    }

    private static RouteFault Many(string what) => Many(MaxConditions, what);

    private static RouteFault Many(int most, string what) =>
        new("too-many", $"a rule carries at most {most} {what}");

    private static string? Head(string value, string mark) =>
        value.StartsWith(mark, StringComparison.OrdinalIgnoreCase) ? value[mark.Length..].Trim() : null;

    private static bool Key(string value) =>
        value.Length is > 0 and <= 64
        && value.All(one => char.IsAsciiLetterOrDigit(one) || one is '-' or '_' or '.');

    private static bool Word(string value) =>
        value.Length is > 0 and <= MaxDomainLength
        && value.All(one => char.IsLetterOrDigit(one) || one is '-' or '_' or '.');

    private static bool Domain(string value) =>
        value.Length <= MaxDomainLength
        && value[0] != '.'
        && value[^1] != '.'
        && value.Contains('.', StringComparison.Ordinal)
        && value.All(one => char.IsLetterOrDigit(one) || one is '-' or '_' or '.');
}
