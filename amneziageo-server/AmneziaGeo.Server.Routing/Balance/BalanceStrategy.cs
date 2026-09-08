namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// How a balancer picks the outbound a packet leaves through.
/// </summary>
public static class BalanceStrategy
{
    /// <summary>
    /// Takes the first member that is alive.
    /// </summary>
    public const string Priority = "priority";

    /// <summary>
    /// Hands the connections to the live members one after another.
    /// </summary>
    public const string Round = "round";

    /// <summary>
    /// Keeps one client on the member its address falls on.
    /// </summary>
    public const string Sticky = "sticky";

    /// <summary>
    /// Every strategy a balancer takes.
    /// </summary>
    public static readonly string[] All = [Priority, Round, Sticky];

    /// <summary>
    /// Tells whether a name is a strategy the server knows.
    /// </summary>
    public static bool Known(string? strategy) => strategy is not null && Array.IndexOf(All, strategy) >= 0;

    /// <summary>
    /// Tells whether a strategy spreads the traffic over every live member.
    /// </summary>
    public static bool Spreads(string? strategy) => strategy is Round or Sticky;
}
