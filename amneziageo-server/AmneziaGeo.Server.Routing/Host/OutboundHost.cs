using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Carrier;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Probe;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Puts the outbounds the panel holds on the host and reads back what the host carries.
/// </summary>
public sealed class OutboundHost
{
    private readonly IHostNetwork _network;

    private readonly IAwgDevices _devices;

    private readonly TimeProvider _time;

    private readonly CarrierHost? _carriers;

    private readonly ProbeLive? _probes;

    /// <summary>
    /// ctor
    /// </summary>
    public OutboundHost(
        IHostNetwork network,
        IAwgDevices devices,
        TimeProvider? time = null,
        CarrierHost? carriers = null,
        ProbeLive? probes = null)
    {
        _network = network;
        _devices = devices;
        _time = time ?? TimeProvider.System;
        _carriers = carriers;
        _probes = probes;
    }

    /// <summary>
    /// Puts one outbound on the host, or takes it off when it is turned off.
    /// </summary>
    public async Task<OutboundState> ApplyAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        try
        {
            await _network.SealAsync(ct).ConfigureAwait(false);
            if (!outbound.IsEnabled)
            {
                await TakeOffAsync(outbound, ct).ConfigureAwait(false);

                return OutboundState.Missing(outbound.Name);
            }

            if (OutboundKind.HasLink(outbound.Kind))
            {
                await RaiseAsync(outbound, ct).ConfigureAwait(false);
            }

            await _network
                .RuleAsync(outbound.Mark, outbound.Table, OutboundRules.PriorityOf(outbound.Mark), true, ct)
                .ConfigureAwait(false);

            return State(outbound);
        }
        catch (Exception ex)
            when (ex is HostNetworkException or NetlinkException or SocketException or InvalidOperationException)
        {
            return OutboundState.Missing(outbound.Name, ex.Message);
        }
    }

    /// <summary>
    /// Takes one outbound off the host.
    /// </summary>
    public async Task WithdrawAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        await _network.SealAsync(ct).ConfigureAwait(false);
        await TakeOffAsync(outbound, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Brings up the interface of an outbound the host holds down or lost, telling whether it had to.
    /// </summary>
    public async Task<bool> MendAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        if (!outbound.IsEnabled || !OutboundKind.HasLink(outbound.Kind))
        {
            return false;
        }

        // Lays a gone interface again with its keys and its carrier.
        if (!_network.HasLink(outbound.Name))
        {
            await RaiseAsync(outbound, ct).ConfigureAwait(false);

            return true;
        }

        if (!IsDown(outbound))
        {
            return false;
        }

        await _network.AddressAsync(outbound.Name, OutboundDevice.Hosts(outbound.Address), ct).ConfigureAwait(false);
        await _network.UpAsync(outbound.Name, outbound.Mtu, ct).ConfigureAwait(false);
        await _network.RouteAsync(outbound.Name, outbound.Table, ct).ConfigureAwait(false);

        return true;
    }

    private async Task TakeOffAsync(OutboundConfig outbound, CancellationToken ct)
    {
        await _network
            .RuleAsync(outbound.Mark, outbound.Table, OutboundRules.PriorityOf(outbound.Mark), false, ct)
            .ConfigureAwait(false);

        if (!OutboundKind.HasLink(outbound.Kind))
        {
            return;
        }

        _carriers?.Withdraw(outbound.Name);
        await _network.ClearRouteAsync(outbound.Table, ct).ConfigureAwait(false);
        if (_network.HasLink(outbound.Name))
        {
            await _network.RemoveLinkAsync(outbound.Name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Puts every outbound the panel holds on the host and returns what the host carries.
    /// </summary>
    public async Task<IReadOnlyList<OutboundState>> SyncAsync(
        IReadOnlyList<OutboundConfig> outbounds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        var states = new List<OutboundState>(outbounds.Count);
        foreach (var outbound in outbounds)
        {
            states.Add(await ApplyAsync(outbound, ct).ConfigureAwait(false));
        }

        await FirewallAsync(outbounds, ct).ConfigureAwait(false);

        return states;
    }

    /// <summary>
    /// Puts the rules that let traffic out through the outbounds on the host.
    /// </summary>
    public async Task FirewallAsync(IReadOnlyList<OutboundConfig> outbounds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        var uplink = await _network.UplinkAsync(ct).ConfigureAwait(false);
        await _network.FirewallAsync(OutboundRuleset.Text(outbounds, uplink), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns what the host holds for one outbound.
    /// </summary>
    public OutboundState State(OutboundConfig outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        var reading = _probes?.Find(outbound.Name);
        if (!OutboundKind.HasLink(outbound.Kind))
        {
            return new OutboundState(outbound.Name, true, string.Empty, null, 0, 0, true, string.Empty, reading)
            {
                Counted = false,
            };
        }

        try
        {
            var state = OutboundDevice.State(outbound, _devices.Find(outbound.Name), _time.GetUtcNow()) with
            {
                Probe = reading,
            };

            if (outbound.IsEnabled && !_network.HasLink(outbound.Name))
            {
                return state with { IsAlive = false, Fault = $"the interface '{outbound.Name}' is gone" };
            }

            return IsDown(outbound)
                ? state with { IsAlive = false, Fault = $"the interface '{outbound.Name}' is down" }
                : state;
        }
        catch (Exception ex) when (ex is NetlinkException or IOException or InvalidOperationException)
        {
            return OutboundState.Missing(outbound.Name, ex.Message);
        }
    }

    /// <summary>
    /// Returns what the host holds for every outbound.
    /// </summary>
    public IReadOnlyList<OutboundState> States(IReadOnlyList<OutboundConfig> outbounds)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        return [.. outbounds.Select(State)];
    }

    /// <summary>
    /// Names the outbounds the host carries traffic through right now.
    /// </summary>
    public IReadOnlyList<string> Carrying(IReadOnlyList<OutboundConfig> outbounds)
    {
        ArgumentNullException.ThrowIfNull(outbounds);

        return [.. States(outbounds).Where(state => state.Carries).Select(state => state.Name)];
    }

    private async Task RaiseAsync(OutboundConfig outbound, CancellationToken ct)
    {
        var server = await ServerAsync(outbound, ct).ConfigureAwait(false);
        if (!_network.HasLink(outbound.Name))
        {
            await _network.AddLinkAsync(outbound.Name, ct).ConfigureAwait(false);
        }

        _devices.Apply(OutboundDevice.Update(outbound, server, !Holds(outbound)));
        await _network.AddressAsync(outbound.Name, OutboundDevice.Hosts(outbound.Address), ct).ConfigureAwait(false);
        await _network.UpAsync(outbound.Name, outbound.Mtu, ct).ConfigureAwait(false);
        await _network.RouteAsync(outbound.Name, outbound.Table, ct).ConfigureAwait(false);
    }

    private bool IsDown(OutboundConfig outbound) =>
        OutboundKind.HasLink(outbound.Kind) && _network.HasLink(outbound.Name) && !_network.IsUp(outbound.Name);

    private bool Holds(OutboundConfig outbound) =>
        _devices.Find(outbound.Name)?.Peers.All(peer => peer.PublicKey == outbound.PeerKey) == true;

    private async Task<IPEndPoint> ServerAsync(OutboundConfig outbound, CancellationToken ct)
    {
        if (!OutboundKind.HasProxy(outbound.Kind))
        {
            var address = await HostAddress.ResolveAsync(outbound.Host, ct).ConfigureAwait(false);

            return new IPEndPoint(address, outbound.Port);
        }

        var carriers = _carriers
            ?? throw new HostNetworkException($"'{outbound.Name}' asks for a websocket the panel does not carry");
        var port = await carriers.RaiseAsync(outbound, ct).ConfigureAwait(false);

        return new IPEndPoint(IPAddress.Loopback, port);
    }
}
