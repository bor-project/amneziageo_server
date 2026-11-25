using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Selects resolver servers and their route.
/// </summary>
public sealed record DnsResolution(IReadOnlyList<string> Servers, DnsWay Way)
{
    public string Device { get; init; } = string.Empty;

    public IReadOnlyList<string> SourceAddresses { get; init; } = [];

    public string Key => $"{Way.Mark}:{Way.Outbound}:{Device}\n{string.Join('\n', SourceAddresses)}\n{string.Join('\n', Servers)}";
}

/// <summary>
/// Identifies a client interface.
/// </summary>
public sealed record DnsOrigin(IPNetwork Network, string Inbound);

/// <summary>
/// Selects DNS policies from routing rules.
/// </summary>
public static class DnsRouting
{
    public static DnsResolution Select(
        RoutePlan? plan,
        string name,
        IPAddress? client = null,
        string? inbound = null,
        long turn = 0,
        long? rule = null)
    {
        var leg = rule is { } id
            ? plan?.Legs.FirstOrDefault(one => one.Rule.Id == id && one.IsOnHost)
            : plan?.Legs.FirstOrDefault(one => Matches(one, name, client, inbound));
        if (leg is null)
        {
            return Closed("no-dns-rule", "no routing entry selects a name server");
        }

        if (leg.IsBlock)
        {
            return Closed("dns-blocked", "the routing entry blocks the name");
        }

        var criterion = leg.Rule.Criteria.FirstOrDefault(one =>
            one.Targets.Count == 0 || leg.DnsCriteria.TryGetValue(one.Id, out var matcher) && matcher.Matches(name));
        var inherited = criterion?.DnsMode == TrafficDnsMode.Default;
        var servers = criterion?.Dns ?? leg.Rule.Dns;
        var members = criterion?.DnsMembers ?? leg.Rule.DnsMembers;
        if (leg.IsDirect)
        {
            return Resolve(inherited ? plan!.HostDns : servers, DnsWay.Host, inherited);
        }

        var ways = plan!.Ways;
        var balancer = ways.Balancer(leg.Rule.Outbound);
        var picked = balancer is { Strategy: BalanceStrategy.Sticky } && client is not null
            ? DnsSticky.Way(balancer, ways, client)
            : DnsExit.Way(leg.Rule.Outbound, ways, turn);
        if (picked.Fault is not null || picked.Mark is null)
        {
            return new DnsResolution([], picked);
        }

        var selected = ways.Outbound(picked.Outbound);
        if (inherited)
        {
            var defaults = balancer is { Dns.Count: > 0 } ? balancer.Dns
                : selected is { Dns.Count: > 0 } ? selected.Dns
                : selected?.Kind == OutboundKind.Local ? plan.HostDns
                : [];

            return Through(Resolve(defaults, picked, true), selected);
        }

        return Through(Resolve(members.TryGetValue(picked.Outbound, out var specific) ? specific : servers, picked, false), selected);
    }

    private static DnsResolution Through(DnsResolution resolution, OutboundConfig? outbound) =>
        outbound is not null && OutboundKind.HasLink(outbound.Kind)
            ? resolution with { Device = outbound.Name, SourceAddresses = outbound.Address }
            : resolution;

    private static DnsResolution Resolve(IReadOnlyList<string> servers, DnsWay way, bool inherited) =>
        servers.Count == 0
            ? Closed(inherited ? "no-default-dns" : "no-rule-dns", "the selected DNS source has no name server")
            : new DnsResolution(servers, way);

    private static DnsResolution Closed(string code, string message) =>
        new([], DnsWay.Closed(code, message));

    private static bool Matches(RouteLeg leg, string name, IPAddress? client, string? inbound)
    {
        var rule = leg.Rule;
        if (!leg.IsOnHost)
        {
            return false;
        }

        if (rule.Clients.Count > 0 && (client is null || !RouteProbe.Within(leg.Sources4.Concat(leg.Sources6), client)))
        {
            return false;
        }

        if (rule.Inbounds.Count > 0 && (inbound is null || !rule.Inbounds.Contains(inbound, StringComparer.Ordinal)))
        {
            return false;
        }

        return !rule.HasTargets || (leg.DnsMatcher ?? new DomainMatcher(leg.Domains)).Matches(name);
    }
}
