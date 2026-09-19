using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// The way out of the host the resolver asks its questions through.
/// </summary>
public static class DnsExit
{
    /// <summary>
    /// Returns the way out a question takes, handing the live members of a round balancer out by the turn.
    /// </summary>
    public static DnsWay Way(DnsSettings settings, RouteWays ways, long turn = 0)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ways);

        var name = settings.Outbound;
        if (name.Length == 0)
        {
            return DnsWay.Host;
        }

        if (ways.Outbound(name) is { } outbound)
        {
            if (!outbound.IsEnabled)
            {
                return DnsWay.Closed("outbound-off", $"the outbound '{name}' is turned off");
            }

            return ways.IsAlive(name)
                ? new DnsWay(outbound.Mark, name, null)
                : new DnsWay(outbound.Mark, name, new DnsFault("outbound-down", $"the outbound '{name}' carries no traffic"));
        }

        return ways.Balancer(name) is { } balancer
            ? Spread(balancer, ways, turn)
            : DnsWay.Closed("unknown-outbound", $"there is no outbound or balancer called '{name}'");
    }

    /// <summary>
    /// Tells whether the panel holds an outbound or a balancer under a name.
    /// </summary>
    public static bool Knows(string name, IReadOnlyList<OutboundConfig> outbounds, IReadOnlyList<Balancer> balancers)
    {
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(balancers);

        var ways = new RouteWays(outbounds, balancers, null);

        return ways.Outbound(name) is not null || ways.Balancer(name) is not null;
    }

    private static DnsWay Spread(Balancer balancer, RouteWays ways, long turn)
    {
        if (!balancer.IsEnabled)
        {
            return DnsWay.Closed("balancer-off", $"the balancer '{balancer.Name}' is turned off");
        }

        var members = balancer.Members
            .Select(ways.Outbound)
            .OfType<OutboundConfig>()
            .Where(member => member.IsEnabled)
            .ToArray();

        if (members.Length == 0)
        {
            return DnsWay.Closed("no-member", $"the balancer '{balancer.Name}' holds no outbound that is turned on");
        }

        var live = members.Where(member => ways.IsAlive(member.Name)).ToArray();
        if (live.Length == 0)
        {
            var message = $"nothing the balancer '{balancer.Name}' picks from carries traffic";

            return new DnsWay(members[0].Mark, members[0].Name, new DnsFault("no-live-member", message));
        }

        var pick = balancer.Strategy == BalanceStrategy.Round
            ? live[(int)(unchecked((ulong)turn) % (ulong)live.Length)]
            : live[0];

        return new DnsWay(pick.Mark, pick.Name, null);
    }
}
