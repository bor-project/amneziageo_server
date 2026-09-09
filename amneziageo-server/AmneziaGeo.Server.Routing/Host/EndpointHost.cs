using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// What raising the interface of an endpoint produced.
/// </summary>
/// <param name="Name">The interface the endpoint runs on.</param>
/// <param name="IsDone">Whether the host took the endpoint.</param>
/// <param name="Message">What the host answered when it refused.</param>
public sealed record EndpointSync(string Name, bool IsDone, string Message)
{
    /// <summary>
    /// Returns the answer of a host that took the endpoint.
    /// </summary>
    public static EndpointSync Done(string name) => new(name, true, string.Empty);
}

/// <summary>
/// Raises the interfaces of the endpoints the panel holds and lays the rules their clients travel by.
/// </summary>
public sealed class EndpointHost
{
    private readonly IHostNetwork _network;

    private readonly IAwgDevices _devices;

    /// <summary>
    /// ctor
    /// </summary>
    public EndpointHost(IHostNetwork network, IAwgDevices devices)
    {
        _network = network;
        _devices = devices;
    }

    /// <summary>
    /// Puts one endpoint on its interface, or takes the interface off when the endpoint is turned off.
    /// </summary>
    public async Task<EndpointSync> ApplyAsync(ServerConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            if (!config.IsEnabled)
            {
                await WithdrawAsync(config.Name, ct).ConfigureAwait(false);

                return EndpointSync.Done(config.Name);
            }

            await _network.ForwardingAsync(ct).ConfigureAwait(false);
            if (!_network.HasLink(config.Name))
            {
                await _network.AddLinkAsync(config.Name, ct).ConfigureAwait(false);
            }

            _devices.Apply(ConfigDevice.Update(config));
            await _network.AddressAsync(config.Name, config.Address, ct).ConfigureAwait(false);
            await _network.UpAsync(config.Name, config.Mtu, ct).ConfigureAwait(false);

            return EndpointSync.Done(config.Name);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or NetlinkException or SocketException or IOException
                or UnauthorizedAccessException or InvalidOperationException)
        {
            return new EndpointSync(config.Name, false, ex.Message);
        }
    }

    /// <summary>
    /// Takes the interface of an endpoint off the host.
    /// </summary>
    public async Task WithdrawAsync(string name, CancellationToken ct)
    {
        if (_network.HasLink(name))
        {
            await _network.RemoveLinkAsync(name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts every endpoint on the host and lays the rules that carry their clients.
    /// </summary>
    public async Task<IReadOnlyList<EndpointSync>> SyncAsync(
        IReadOnlyList<ServerConfig> configs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var done = new List<EndpointSync>(configs.Count);
        foreach (var config in configs)
        {
            done.Add(await ApplyAsync(config, ct).ConfigureAwait(false));
        }

        done.Add(await FirewallAsync(configs, ct).ConfigureAwait(false));

        return done;
    }

    /// <summary>
    /// Lays the rules that masquerade the clients and hold them out of the closed ranges.
    /// </summary>
    public async Task<EndpointSync> FirewallAsync(IReadOnlyList<ServerConfig> configs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configs);

        try
        {
            var uplink = await _network.UplinkAsync(ct).ConfigureAwait(false);
            await _network.FirewallAsync(EndpointRuleset.Text(configs, uplink), ct).ConfigureAwait(false);

            return EndpointSync.Done(EndpointRuleset.TableName);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new EndpointSync(EndpointRuleset.TableName, false, ex.Message);
        }
    }
}
