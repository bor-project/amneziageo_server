namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// A routing list the panel hands to the clients whose template names it, so their applications route by it.
/// </summary>
public sealed record RoutingPreset
{
    /// <summary>
    /// The number the panel holds the preset under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the list takes in the application of the client.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The geo keys, networks, addresses and domains that go through the tunnel.
    /// </summary>
    public IReadOnlyList<string> Proxy { get; init; } = [];

    /// <summary>
    /// The geo keys, networks, addresses and domains that go past the tunnel.
    /// </summary>
    public IReadOnlyList<string> Direct { get; init; } = [];

    /// <summary>
    /// The geo keys, networks, addresses and domains the client blocks.
    /// </summary>
    public IReadOnlyList<string> Block { get; init; } = [];

    /// <summary>
    /// Whether every UDP packet goes through the tunnel.
    /// </summary>
    public bool AllUdp { get; init; }

    /// <summary>
    /// Whether everything goes through the tunnel but what goes past it.
    /// </summary>
    public bool Full { get; init; }

    /// <summary>
    /// When the preset was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the preset was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
