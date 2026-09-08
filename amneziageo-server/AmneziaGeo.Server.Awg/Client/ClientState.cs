using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// What the host holds for one client.
/// </summary>
/// <param name="Name">The name the client is listed under.</param>
/// <param name="PublicKey">The public key of the client.</param>
/// <param name="IsOnline">Whether the client is connected.</param>
/// <param name="IsPresent">Whether the interface carries the client.</param>
/// <param name="LastHandshake">When the client last completed a handshake.</param>
/// <param name="RxBytes">Bytes taken from the client.</param>
/// <param name="TxBytes">Bytes given to the client.</param>
/// <param name="Endpoint">The address the last packet of the client came from.</param>
public sealed record ClientState(
    string Name,
    string PublicKey,
    bool IsOnline,
    bool IsPresent,
    DateTimeOffset? LastHandshake,
    ulong RxBytes,
    ulong TxBytes,
    string Endpoint)
{
    /// <summary>
    /// How long a client counts as connected after a handshake.
    /// </summary>
    public static readonly TimeSpan AliveFor = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Returns the state of a client the interface does not carry.
    /// </summary>
    public static ClientState Missing(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new ClientState(client.Name, client.PublicKey, false, false, null, 0, 0, string.Empty);
    }

    /// <summary>
    /// Returns the state of a client out of the peer the kernel holds.
    /// </summary>
    public static ClientState Of(TunnelClient client, AwgPeer? peer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (peer is null)
        {
            return Missing(client);
        }

        var handshake = peer.LastHandshake;

        return new ClientState(
            client.Name,
            client.PublicKey,
            handshake is not null && now - handshake.Value <= AliveFor,
            true,
            handshake,
            peer.RxBytes,
            peer.TxBytes,
            peer.Endpoint?.ToString() ?? string.Empty);
    }
}
