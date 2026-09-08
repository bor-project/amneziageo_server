namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// What a balancer carries before anyone fills it in.
/// </summary>
public static class BalanceDefaults
{
    /// <summary>
    /// How often the server reads back which members carry traffic.
    /// </summary>
    public static readonly TimeSpan Watch = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Returns an empty balancer under a name.
    /// </summary>
    public static Balancer Fresh(string name) => new()
    {
        Name = name,
        Strategy = BalanceStrategy.Priority,
        IsEnabled = true,
    };
}
