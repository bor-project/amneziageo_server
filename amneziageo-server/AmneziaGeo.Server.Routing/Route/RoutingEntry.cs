namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Maps a traffic rule to a processing object.
/// </summary>
public sealed record RoutingEntry
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public int Position { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string Action { get; init; } = RouteAction.Out;
    public string Outbound { get; init; } = string.Empty;
    public IReadOnlyList<long> RuleIds { get; init; } = [];
    public IReadOnlyList<string> Clients { get; init; } = [];
    public IReadOnlyList<string> Inbounds { get; init; } = [];
    public IReadOnlyList<string> Ports { get; init; } = [];
    public IReadOnlyList<string> SourcePorts { get; init; } = [];
    public string Protocol { get; init; } = RouteProtocol.Any;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
