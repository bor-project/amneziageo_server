using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Puts the outbounds the panel holds on the host and reads back what the host carries.
/// </summary>
public sealed class OutboundHost
{
    private readonly IHostNetwork _network;

    private readonly IAwgDevices _devices;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public OutboundHost(IHostNetwork network, IAwgDevices devices, TimeProvider? time = null)
    {
        _network = network;
        _devices = devices;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Puts one outbound on the host, or takes it off when it is turned off.
    /// </summary>
    public async Task<OutboundState> ApplyAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);

        try
        {
            if (!outbound.IsEnabled)
            {
                await WithdrawAsync(outbound, ct).ConfigureAwait(false);

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

        await _network
            .RuleAsync(outbound.Mark, outbound.Table, OutboundRules.PriorityOf(outbound.Mark), false, ct)
            .ConfigureAwait(false);

        if (!OutboundKind.HasLink(outbound.Kind))
        {
            return;
        }

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

        if (!OutboundKind.HasLink(outbound.Kind))
        {
            return new OutboundState(outbound.Name, true, string.Empty, null, 0, 0, true, string.Empty);
        }

        try
        {
            return OutboundDevice.State(outbound, _devices.Find(outbound.Name), _time.GetUtcNow());
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

    private async Task RaiseAsync(OutboundConfig outbound, CancellationToken ct)
    {
        var server = await ResolveAsync(outbound.Host, ct).ConfigureAwait(false);
        if (!_network.HasLink(outbound.Name))
        {
            await _network.AddLinkAsync(outbound.Name, ct).ConfigureAwait(false);
        }

        _devices.Apply(OutboundDevice.Update(outbound, server));
        await _network.AddressAsync(outbound.Name, outbound.Address, ct).ConfigureAwait(false);
        await _network.UpAsync(outbound.Name, outbound.Mtu, ct).ConfigureAwait(false);
        await _network.RouteAsync(outbound.Name, outbound.Table, ct).ConfigureAwait(false);
    }

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var found = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        return found.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork)
            ?? found.FirstOrDefault()
            ?? throw new HostNetworkException($"'{host}' does not resolve to an address");
    }
}
