using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Client;
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
/// <param name="Fault">The kind of refusal, one of <see cref="EndpointFault"/>, empty when the host took the endpoint.</param>
public sealed record EndpointSync(string Name, bool IsDone, string Message, string Fault = "")
{
    /// <summary>
    /// Returns the answer of a host that took the endpoint.
    /// </summary>
    public static EndpointSync Done(string name) => new(name, true, string.Empty);
}

/// <summary>
/// The kinds of refusal a host gives when it does not raise an endpoint.
/// </summary>
public static class EndpointFault
{
    /// <summary>
    /// Another program holds the port of the endpoint.
    /// </summary>
    public const string PortBusy = "port-busy";

    /// <summary>
    /// The kernel carries no AmneziaWG module.
    /// </summary>
    public const string NoModule = "no-module";

    /// <summary>
    /// The panel lacks the right to change the network of the host.
    /// </summary>
    public const string NoRights = "no-rights";

    /// <summary>
    /// The host keeps packet forwarding off.
    /// </summary>
    public const string Forwarding = "forwarding-off";

    /// <summary>
    /// The host refused for another reason.
    /// </summary>
    public const string Failed = "raise-failed";
}

/// <summary>
/// Raises the interfaces of the endpoints the panel holds and lays the rules their clients travel by.
/// </summary>
public sealed class EndpointHost
{
    private const int NotPermitted = 1;

    private const int AccessDenied = 13;

    private const int AddressInUse = 98;

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
            return new EndpointSync(config.Name, false, ex.Message, Fault(ex));
        }
    }

    /// <summary>
    /// Names the kind of refusal a host gave.
    /// </summary>
    public static string Fault(Exception ex) => ex switch
    {
        NetlinkException { Error: AddressInUse } => EndpointFault.PortBusy,
        SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } => EndpointFault.PortBusy,
        NetlinkException { Error: NotPermitted or AccessDenied } => EndpointFault.NoRights,
        UnauthorizedAccessException => EndpointFault.NoRights,
        HostNetworkException { InnerException: IOException or UnauthorizedAccessException } => EndpointFault.Forwarding,
        _ when Says(ex, "Address already in use") => EndpointFault.PortBusy,
        _ when Says(ex, "Operation not permitted") => EndpointFault.NoRights,
        _ when Says(ex, "Unknown device type") || Says(ex, "Operation not supported") || Says(ex, "does not carry")
            => EndpointFault.NoModule,
        _ => EndpointFault.Failed,
    };

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
        IReadOnlyList<TunnelClient> clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var done = new List<EndpointSync>(configs.Count);
        foreach (var config in configs)
        {
            done.Add(await ApplyAsync(config, ct).ConfigureAwait(false));
        }

        done.Add(await FirewallAsync(configs, clients, ct).ConfigureAwait(false));

        return done;
    }

    /// <summary>
    /// Lays the rules that masquerade the clients, hold them out of the closed ranges and carry what reaches them
    /// back.
    /// </summary>
    public async Task<EndpointSync> FirewallAsync(
        IReadOnlyList<ServerConfig> configs,
        IReadOnlyList<TunnelClient> clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configs);

        try
        {
            var uplink = await _network.UplinkAsync(ct).ConfigureAwait(false);
            await _network.FirewallAsync(EndpointRuleset.Text(configs, clients, uplink), ct).ConfigureAwait(false);

            return EndpointSync.Done(EndpointRuleset.TableName);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return new EndpointSync(EndpointRuleset.TableName, false, ex.Message);
        }
    }

    private static bool Says(Exception ex, string words) =>
        ex.Message.Contains(words, StringComparison.OrdinalIgnoreCase);
}
