using AmneziaGeo.Server.Routing.Balance;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// The marks that send the traffic of a rule out, and how one of them is picked.
/// </summary>
/// <param name="Strategy">How a mark is picked when the rule leaves through a balancer.</param>
/// <param name="Marks">The marks of the outbounds the traffic leaves through.</param>
public sealed record RouteExit(string Strategy, IReadOnlyList<uint> Marks)
{
    /// <summary>
    /// The way out of a rule that sends nothing anywhere.
    /// </summary>
    public static readonly RouteExit None = new(BalanceStrategy.Priority, []);

    /// <summary>
    /// The seed the addresses of the clients are spread with.
    /// </summary>
    public uint Seed { get; init; }

    /// <summary>
    /// The mark the traffic carries when there is one to carry.
    /// </summary>
    public uint Mark => Marks.Count > 0 ? Marks[0] : 0;

    /// <summary>
    /// Tells whether the traffic is spread over more than one outbound.
    /// </summary>
    public bool IsSpread => Marks.Count > 1 && BalanceStrategy.Spreads(Strategy);

    /// <summary>
    /// Returns the way out through a single outbound.
    /// </summary>
    public static RouteExit One(uint mark) => new(BalanceStrategy.Priority, [mark]);
}
