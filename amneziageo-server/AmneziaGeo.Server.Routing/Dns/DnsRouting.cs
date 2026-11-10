using System.Net;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Selects resolver servers and their route.
/// </summary>
public sealed record DnsResolution(IReadOnlyList<string> Servers, DnsWay Way)
{
    public string Key => $"{Way.Mark}:{Way.Outbound}\n{string.Join('\n', Servers)}";
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
        DnsSettings settings,
        RoutePlan? plan,
        string name,
        IPAddress? client = null,
        string? inbound = null,
        long turn = 0,
        long? rule = null)
    {
        var ways = plan?.Ways ?? RouteWays.None;
        if (settings.Selection == DnsSelection.Outbound)
        {
            return Through(settings.Outbound, [], settings, ways, client, turn, true);
        }

        if (settings.Selection != DnsSelection.Routing || plan is null)
        {
            return Global(settings, ways, turn);
        }

        var leg = rule is { } id
            ? plan.Legs.FirstOrDefault(one => one.Rule.Id == id && one.IsOnHost)
            : plan.Legs.FirstOrDefault(one => Matches(one, name, client, inbound));
        if (leg is null)
        {
            return Global(settings, ways, turn);
        }

        var servers = leg.Rule.Groups
            .Select(id => plan.Groups.FirstOrDefault(group => group.Id == id))
            .OfType<AddressGroup>()
            .FirstOrDefault(group => group.Dns.Count > 0 && GroupMatches(group, name, plan))?.Dns ?? [];
        if (leg.Rule.Action != RouteAction.Out)
        {
            return leg.IsDirect
                ? new DnsResolution(servers.Count > 0 ? servers : settings.Upstreams, DnsWay.Host)
                : Global(settings, ways, turn);
        }

        return Through(leg.Rule.Outbound, servers, settings, ways, client, turn, false);
    }

    private static DnsResolution Global(DnsSettings settings, RouteWays ways, long turn) =>
        new(settings.Upstreams, DnsExit.Way(settings, ways, turn));

    private static DnsResolution Through(
        string name,
        IReadOnlyList<string> servers,
        DnsSettings settings,
        RouteWays ways,
        IPAddress? client,
        long turn,
        bool required)
    {
        var balancer = ways.Balancer(name);
        var picked = balancer is { Strategy: BalanceStrategy.Sticky } && client is not null
            ? DnsSticky.Way(balancer, ways, client)
            : DnsExit.Way(settings with { Outbound = name }, ways, turn);
        if (picked.Fault is not null || picked.Mark is null)
        {
            return new DnsResolution([], picked);
        }

        var chosen = servers.Count > 0 ? servers
            : balancer is { Dns.Count: > 0 } ? balancer.Dns
            : ways.Outbound(picked.Outbound)?.Dns ?? [];
        if (chosen.Count > 0)
        {
            return new DnsResolution(chosen, picked);
        }

        return required
            ? new DnsResolution([], DnsWay.Closed("no-outbound-dns", $"'{name}' has no name server"))
            : new DnsResolution(settings.Upstreams, picked);
    }

    private static bool Matches(RouteLeg leg, string name, IPAddress? client, string? inbound)
    {
        var rule = leg.Rule;
        if (!leg.IsOnHost || rule.Ports.Count > 0 || rule.SourcePorts.Count > 0 || rule.Protocol != RouteProtocol.Any)
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

    private static bool GroupMatches(AddressGroup group, string name, RoutePlan plan) =>
        plan.DnsGroups.TryGetValue(group.Id, out var matcher) && matcher.Matches(name);

}
