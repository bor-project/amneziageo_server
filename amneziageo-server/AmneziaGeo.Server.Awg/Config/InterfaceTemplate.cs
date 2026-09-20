namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// The settings every endpoint of a template carries, as the panel holds them.
/// </summary>
public sealed record InterfaceTemplate
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
    /// The port a fresh endpoint of the template starts from.
    /// </summary>
    public int ListenPort { get; init; } = ConfigDefaults.ListenPort;

    /// <summary>
    /// The range a fresh endpoint of the template starts from.
    /// </summary>
    public string Subnet { get; init; } = ConfigDefaults.Subnet;

    /// <summary>
    /// The name servers the endpoints give clients.
    /// </summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>
    /// The ranges a client of the endpoints routes into the tunnel.
    /// </summary>
    public IReadOnlyList<string> AllowedIps { get; init; } = [];

    /// <summary>
    /// The packet size the endpoints give clients.
    /// </summary>
    public int Mtu { get; init; }

    /// <summary>
    /// How often a client of the endpoints holds the path open, in seconds.
    /// </summary>
    public int Keepalive { get; init; }

    /// <summary>
    /// How long a device of the endpoints stays silent before it counts as gone, in seconds.
    /// </summary>
    public int OfflineAfter { get; init; } = ConfigDefaults.OfflineAfter;

    /// <summary>
    /// The ranges the endpoints keep their clients out of.
    /// </summary>
    public IReadOnlyList<string> Blocked { get; init; } = [];

    /// <summary>
    /// The client template a client of the endpoints takes, or null for the values of its endpoint.
    /// </summary>
    public long? ClientTemplateId { get; init; }

    /// <summary>
    /// The obfuscation the endpoints carry.
    /// </summary>
    public ObfuscationSettings Obfuscation { get; init; } = new();

    /// <summary>
    /// When the template was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the template was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>
    /// Returns the endpoint with the constants of the template in place of its own.
    /// </summary>
    public ServerConfig Over(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config with
        {
            TemplateId = Id,
            Dns = [.. Dns],
            AllowedIps = [.. AllowedIps],
            Mtu = Mtu,
            Keepalive = Keepalive,
            OfflineAfter = OfflineAfter,
            Blocked = [.. Blocked],
            Obfuscation = Obfuscation,
        };
    }

    /// <summary>
    /// Returns an endpoint of the template with a key pair of its own.
    /// </summary>
    public ServerConfig Fresh(string name) =>
        Over(ConfigDefaults.Fresh(name) with { ListenPort = ListenPort, Address = [Subnet] });

    /// <summary>
    /// Returns the template the settings of an endpoint amount to.
    /// </summary>
    public static InterfaceTemplate Of(string name, ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new InterfaceTemplate
        {
            Name = name,
            ListenPort = config.ListenPort,
            Subnet = config.Address.Count > 0 ? config.Address[0] : ConfigDefaults.Subnet,
            Dns = [.. config.Dns],
            AllowedIps = [.. config.AllowedIps],
            Mtu = config.Mtu,
            Keepalive = config.Keepalive,
            OfflineAfter = config.OfflineAfter,
            Blocked = [.. config.Blocked],
            Obfuscation = config.Obfuscation,
        };
    }
}
