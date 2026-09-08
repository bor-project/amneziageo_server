using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Api.Balancers;

/// <summary>
/// What the host carries for one outbound a balancer picks from.
/// </summary>
public sealed record BalanceMemberBody(string Name, bool IsKnown, bool IsEnabled, bool IsAlive, uint Mark);

/// <summary>
/// What the host carries for one balancer.
/// </summary>
public sealed record BalanceStateBody(bool IsLive, IReadOnlyList<BalanceMemberBody> Members);

/// <summary>
/// One balancer as the panel reads it.
/// </summary>
public sealed record BalanceResponse(
    long Id,
    string Name,
    int Position,
    bool IsEnabled,
    string Strategy,
    IReadOnlyList<string> Members,
    BalanceStateBody State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The settings a balancer is added or changed with.
/// </summary>
public sealed record BalanceRequest(
    string? Name,
    bool IsEnabled,
    string? Strategy,
    IReadOnlyList<string>? Members);

/// <summary>
/// Whether a balancer is on.
/// </summary>
public sealed record BalanceSwitchRequest(bool On);

/// <summary>
/// Turns balancers into what the panel reads and back.
/// </summary>
public static class BalanceAnswers
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Returns one balancer with what the host carries for the outbounds it picks from.
    /// </summary>
    public static BalanceResponse Balancer(
        Balancer balancer,
        IReadOnlyList<OutboundConfig> outbounds,
        IReadOnlyList<OutboundState> states)
    {
        ArgumentNullException.ThrowIfNull(balancer);
        ArgumentNullException.ThrowIfNull(outbounds);
        ArgumentNullException.ThrowIfNull(states);

        var members = balancer.Members.Select(name => Member(name, outbounds, states)).ToArray();

        return new BalanceResponse(
            balancer.Id,
            balancer.Name,
            balancer.Position,
            balancer.IsEnabled,
            balancer.Strategy,
            balancer.Members,
            new BalanceStateBody(balancer.IsEnabled && members.Any(member => member.IsAlive), members),
            balancer.CreatedUtc,
            balancer.UpdatedUtc);
    }

    /// <summary>
    /// Returns what a request asks a balancer to become.
    /// </summary>
    public static Balancer Draft(BalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new Balancer
        {
            Name = (request.Name ?? string.Empty).Trim(),
            IsEnabled = request.IsEnabled,
            Strategy = (request.Strategy ?? BalanceStrategy.Priority).Trim(),
            Members = Clean(request.Members),
        };
    }

    private static BalanceMemberBody Member(
        string name,
        IReadOnlyList<OutboundConfig> outbounds,
        IReadOnlyList<OutboundState> states)
    {
        var at = -1;
        for (var index = 0; index < outbounds.Count; index++)
        {
            if (string.Equals(outbounds[index].Name, name, StringComparison.Ordinal))
            {
                at = index;
                break;
            }
        }

        if (at < 0)
        {
            return new BalanceMemberBody(name, false, false, false, 0);
        }

        var outbound = outbounds[at];
        var alive = outbound.IsEnabled && at < states.Count && states[at].IsAlive;

        return new BalanceMemberBody(name, true, outbound.IsEnabled, alive, outbound.Mark);
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
