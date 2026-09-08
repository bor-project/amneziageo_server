namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// One group of outbounds the rules leave through, as the panel holds it.
/// </summary>
public sealed record Balancer
{
    /// <summary>
    /// The number the panel keeps the balancer under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the rules call the balancer by.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Where the balancer stands among the others.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Whether the rules that name the balancer go on the host.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// How the balancer picks the outbound.
    /// </summary>
    public string Strategy { get; init; } = BalanceStrategy.Priority;

    /// <summary>
    /// The outbounds the balancer picks from, in the order they are taken.
    /// </summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>
    /// When the balancer was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the balancer was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
