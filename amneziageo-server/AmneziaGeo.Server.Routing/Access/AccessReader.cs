using System.Net;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Turns a logged packet into a record with the names the panel knows.
/// </summary>
public static class AccessReader
{
    /// <summary>
    /// Returns the record of a packet, or null when the packet is not one the panel logged.
    /// </summary>
    public static AccessRecord? Record(
        NetfilterPacket packet,
        DateTimeOffset at,
        RoutePlan? plan,
        Func<IPAddress, string> client,
        Func<uint, string> inbound,
        AccessNames names)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(names);

        if (AccessTag.Read(packet.Prefix) is not { } decision || AccessFlow.Read(packet.Payload) is not { } flow)
        {
            return null;
        }

        var leg = decision.Rule is { } id ? plan?.Legs.FirstOrDefault(one => one.Rule.Id == id) : null;
        var (way, via, path) = Way(decision, leg, plan, packet.Mark);

        return new AccessRecord
        {
            At = at,
            Client = client(flow.Source),
            Source = flow.Source.ToString(),
            SourcePort = flow.SourcePort,
            Inbound = inbound(packet.Inbound),
            Protocol = flow.Protocol,
            Target = flow.Target.ToString(),
            Port = flow.Port,
            Name = names.Name(flow.Source, flow.Target),
            Verdict = decision.Verdict,
            Rule = decision.Rule,
            RuleName = leg?.Rule.Name ?? string.Empty,
            Way = way,
            Via = via,
            Path = path,
        };
    }

    /// <summary>
    /// Tells whether a record is a question to the resolver of the panel, which the log leaves out.
    /// </summary>
    public static bool IsQuestion(AccessRecord record, RoutePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(record);

        return plan is { Dns.IsEnabled: true } && record.Port == 53 && record.Protocol is AccessFlow.Tcp or AccessFlow.Udp;
    }

    private static (string Way, string Via, string Path) Way(
        AccessDecision decision,
        RouteLeg? leg,
        RoutePlan? plan,
        uint mark)
    {
        var named = leg?.Rule.Outbound.Trim() ?? string.Empty;

        return decision.Verdict switch
        {
            AccessVerdict.Guard => (decision.Guard, string.Empty, string.Empty),
            AccessVerdict.Held => (named, string.Empty, string.Empty),
            AccessVerdict.Host => (string.Empty, string.Empty, AccessPath.Local),
            AccessVerdict.Out => Out(plan, named, mark),
            _ => (string.Empty, string.Empty, string.Empty),
        };
    }

    private static (string Way, string Via, string Path) Out(RoutePlan? plan, string named, uint mark)
    {
        var outbound = plan?.Ways.Outbounds.FirstOrDefault(one => one.Mark == mark) ?? plan?.Ways.Outbound(named);
        var path = outbound is null
            ? string.Empty
            : OutboundKind.HasLink(outbound.Kind) ? AccessPath.Relay : AccessPath.Local;

        return (outbound?.Name ?? named, plan?.Ways.Balancer(named)?.Name ?? string.Empty, path);
    }
}
