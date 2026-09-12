namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// What a client takes from the tunnel.
/// </summary>
public enum ClientInbound
{
    /// <summary>
    /// The client answers nothing that comes from the tunnel.
    /// </summary>
    Off = 0,

    /// <summary>
    /// The client answers the server alone.
    /// </summary>
    Server = 1,

    /// <summary>
    /// The client answers everyone on the network of the tunnel.
    /// </summary>
    Network = 2,

    /// <summary>
    /// The client answers whoever the endpoint lets in.
    /// </summary>
    Endpoint = 3,
}

/// <summary>
/// Writes and reads what a client takes from the tunnel.
/// </summary>
public static class InboundName
{
    /// <summary>
    /// The name of taking nothing.
    /// </summary>
    public const string Off = "off";

    /// <summary>
    /// The name of taking the server alone.
    /// </summary>
    public const string Server = "server";

    /// <summary>
    /// The name of taking the whole network of the tunnel.
    /// </summary>
    public const string Network = "network";

    /// <summary>
    /// The name of taking what the endpoint lets in.
    /// </summary>
    public const string Endpoint = "endpoint";

    /// <summary>
    /// Returns the name of what a client takes from the tunnel.
    /// </summary>
    public static string Of(ClientInbound inbound) => inbound switch
    {
        ClientInbound.Server => Server,
        ClientInbound.Network => Network,
        ClientInbound.Endpoint => Endpoint,
        _ => Off,
    };

    /// <summary>
    /// Returns what a client takes, reading the endpoint where the client leaves the choice to it.
    /// </summary>
    public static ClientInbound Taken(ClientInbound client, ClientInbound endpoint) =>
        client == ClientInbound.Endpoint ? endpoint : client;

    /// <summary>
    /// Returns what a name stands for, the given mode where the name is empty and -1 where it is unknown.
    /// </summary>
    public static ClientInbound Read(string? text, ClientInbound otherwise)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return otherwise;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            Off => ClientInbound.Off,
            Server => ClientInbound.Server,
            Network => ClientInbound.Network,
            Endpoint => ClientInbound.Endpoint,
            _ => (ClientInbound)(-1),
        };
    }
}
