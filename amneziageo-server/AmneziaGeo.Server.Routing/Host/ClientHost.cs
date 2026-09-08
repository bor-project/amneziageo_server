using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// What putting the clients of an endpoint on the host produced.
/// </summary>
/// <param name="IsDone">Whether the host took the clients.</param>
/// <param name="Message">What the host answered when it refused.</param>
public sealed record ClientSync(bool IsDone, string Message)
{
    /// <summary>
    /// The answer of a host that took the clients.
    /// </summary>
    public static readonly ClientSync Done = new(true, string.Empty);
}

/// <summary>
/// Puts the clients the panel holds on the interface of an endpoint.
/// </summary>
public sealed class ClientHost
{
    private readonly IHostNetwork _network;

    private readonly IAwgDevices _devices;

    private readonly InterfaceFile _file;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientHost(IHostNetwork network, IAwgDevices devices, InterfaceFile file, TimeProvider? time = null)
    {
        _network = network;
        _devices = devices;
        _file = file;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Puts the clients of an endpoint on its interface and into the file the host boots from.
    /// </summary>
    public async Task<ClientSync> SyncAsync(
        ServerConfig config,
        IReadOnlyList<TunnelClient> clients,
        IReadOnlyList<string> gone,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(gone);

        try
        {
            await _file.WriteAsync(config, clients, ct).ConfigureAwait(false);
            if (!_network.HasLink(config.Name))
            {
                return new ClientSync(false, $"the host carries no interface called '{config.Name}'");
            }

            var away = clients.Where(client => !client.IsEnabled).Select(client => client.PublicKey).Concat(gone);
            _devices.Apply(ClientDevice.Away(config, away));
            _devices.Apply(ClientDevice.Update(config, clients));

            return ClientSync.Done;
        }
        catch (Exception ex)
            when (ex is NetlinkException or SocketException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new ClientSync(false, ex.Message);
        }
    }

    /// <summary>
    /// Returns what the host holds for every client of an endpoint.
    /// </summary>
    public IReadOnlyList<ClientState> States(ServerConfig config, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        var peers = Peers(config);
        if (peers is null)
        {
            return [.. clients.Select(ClientState.Missing)];
        }

        var now = _time.GetUtcNow();

        return [.. clients.Select(client => ClientState.Of(client, peers.GetValueOrDefault(client.PublicKey), now))];
    }

    /// <summary>
    /// Returns the peers the interface of an endpoint carries that the panel does not hold.
    /// </summary>
    public IReadOnlyList<string> Stale(ServerConfig config, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(config);

        var peers = Peers(config);

        return peers is null ? [] : ClientDevice.Stale(peers.Keys, clients);
    }

    private Dictionary<string, AwgPeer>? Peers(ServerConfig config)
    {
        try
        {
            var device = _devices.Find(config.Name);

            return device?.Peers.ToDictionary(peer => peer.PublicKey, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is NetlinkException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
