namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// Shapes the settings of a balancer have to take.
/// </summary>
public static class BalanceRules
{
    /// <summary>
    /// The longest name a balancer takes.
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// How many outbounds a balancer picks from.
    /// </summary>
    public const int MaxMembers = 16;

    /// <summary>
    /// Returns why the settings of a balancer are unusable, or null when they hold.
    /// </summary>
    public static BalanceFault? Check(Balancer balancer)
    {
        ArgumentNullException.ThrowIfNull(balancer);

        return CheckName(balancer.Name)
            ?? CheckStrategy(balancer.Strategy)
            ?? CheckMembers(balancer.Members);
    }

    /// <summary>
    /// Returns why the name of a balancer is unusable, or null when it holds.
    /// </summary>
    public static BalanceFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return new BalanceFault(
                "bad-balancer-name",
                $"the name is empty or longer than {MaxNameLength} characters");
        }

        return name.Any(char.IsControl)
            ? new BalanceFault("bad-balancer-name", "the name carries a character that is not printable")
            : null;
    }

    private static BalanceFault? CheckStrategy(string? strategy) =>
        BalanceStrategy.Known(strategy)
            ? null
            : new BalanceFault("bad-strategy", $"'{strategy}' is not a strategy a balancer takes");

    private static BalanceFault? CheckMembers(IReadOnlyList<string> members)
    {
        if (members.Count == 0)
        {
            return new BalanceFault("no-members", "the balancer names no outbound to pick from");
        }

        if (members.Count > MaxMembers)
        {
            return new BalanceFault("too-many-members", $"a balancer picks from at most {MaxMembers} outbounds");
        }

        if (members.Distinct(StringComparer.Ordinal).Count() != members.Count)
        {
            return new BalanceFault("same-member", "the balancer names one outbound twice");
        }

        return null;
    }
}
