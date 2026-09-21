namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// What the file of a client takes in place of the settings of its endpoint.
/// </summary>
public sealed record ClientTemplate
{
    /// <summary>
    /// The number the panel holds the template under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the template is listed under.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The geo keys, networks, addresses and domains the ranges come from.
    /// </summary>
    public IReadOnlyList<string> Entries { get; init; } = [];

    /// <summary>
    /// The ranges the client routes into the tunnel.
    /// </summary>
    public IReadOnlyList<string> AllowedIps { get; init; } = [];

    /// <summary>
    /// The entries nothing was found for.
    /// </summary>
    public IReadOnlyList<string> Missed { get; init; } = [];

    /// <summary>
    /// The name servers the client asks.
    /// </summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>
    /// The packet size the client takes.
    /// </summary>
    public int? Mtu { get; init; }

    /// <summary>
    /// The seconds between the empty packets the client sends.
    /// </summary>
    public int? Keepalive { get; init; }

    /// <summary>
    /// Whether the application of the client routes on its own.
    /// </summary>
    public bool Routing { get; init; } = TemplateDefaults.Routing;

    /// <summary>
    /// When the ranges were last worked out.
    /// </summary>
    public DateTimeOffset? RefreshedUtc { get; init; }

    /// <summary>
    /// When the template was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the template was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
