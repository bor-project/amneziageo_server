namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// One routing rule, as the panel holds it.
/// </summary>
public sealed record RouteRule
{
    /// <summary>
    /// The number the panel keeps the rule under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the rule is shown under.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Where the rule stands among the others.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Whether the rule is put on the host.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// What happens to the traffic the rule matches.
    /// </summary>
    public string Action { get; init; } = RouteAction.Out;

    /// <summary>
    /// The outbound the traffic leaves through.
    /// </summary>
    public string Outbound { get; init; } = string.Empty;

    /// <summary>
    /// Whether the traffic is held back while the way out of the rule carries nothing.
    /// </summary>
    public bool HoldsWhenDown { get; init; } = true;

    /// <summary>
    /// What the traffic goes to: geo keys, domains and address ranges.
    /// </summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>
    /// The client addresses the traffic comes from.
    /// </summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// The clients of the panel the traffic comes from, by name.
    /// </summary>
    public IReadOnlyList<string> Clients { get; init; } = [];

    /// <summary>
    /// The interfaces the traffic comes in on.
    /// </summary>
    public IReadOnlyList<string> Inbounds { get; init; } = [];

    /// <summary>
    /// The ports the traffic goes to, single or as a range.
    /// </summary>
    public IReadOnlyList<string> Ports { get; init; } = [];

    /// <summary>
    /// The ports the traffic comes from, single or as a range.
    /// </summary>
    public IReadOnlyList<string> SourcePorts { get; init; } = [];

    /// <summary>
    /// The protocol the traffic carries.
    /// </summary>
    public string Protocol { get; init; } = RouteProtocol.Any;

    /// <summary>
    /// When the rule was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the rule was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
