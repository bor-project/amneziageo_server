using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// One client of an endpoint as the panel holds it.
/// </summary>
public sealed record TunnelClient
{
    /// <summary>
    /// The number the panel holds the client under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The endpoint the client connects to.
    /// </summary>
    public long ConfigId { get; init; }

    /// <summary>
    /// The name the client is listed under.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The private key of the client, in base64.
    /// </summary>
    public string PrivateKey { get; init; } = string.Empty;

    /// <summary>
    /// The public key of the client, in base64.
    /// </summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// The key the client adds to the handshake, in base64.
    /// </summary>
    public string PresharedKey { get; init; } = string.Empty;

    /// <summary>
    /// The addresses the client carries in the tunnel.
    /// </summary>
    public IReadOnlyList<string> Address { get; init; } = [];

    /// <summary>
    /// Whether the endpoint takes the client.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// What the client is noted as.
    /// </summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// The template the file of the client takes settings from.
    /// </summary>
    public long? TemplateId { get; init; }

    /// <summary>
    /// Where the name servers of the file of the client come from.
    /// </summary>
    public DnsChoice DnsMode { get; init; }

    /// <summary>
    /// The name servers the file of the client names when it picks its own.
    /// </summary>
    public IReadOnlyList<string> Dns { get; init; } = [];

    /// <summary>
    /// The subscription that hands the client out, empty for none.
    /// </summary>
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>
    /// How many bytes a day the client moves, 0 for no limit.
    /// </summary>
    public long DailyLimit { get; init; }

    /// <summary>
    /// What the client takes from the tunnel.
    /// </summary>
    public ClientInbound Inbound { get; init; }

    /// <summary>
    /// Whether the server takes the tunnel of the client down by its signal.
    /// </summary>
    public bool Disconnect { get; init; } = true;

    /// <summary>
    /// Whether the application of the client said it takes no signal to disconnect.
    /// </summary>
    public bool DisconnectRefused { get; init; }

    /// <summary>
    /// When the client was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the client was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }
}
