namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Groups routing destinations.
/// </summary>
public sealed record AddressGroup
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<string> Targets { get; init; } = [];

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}
