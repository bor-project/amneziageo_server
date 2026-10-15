using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// What laying the interface of an endpoint again produced.
/// </summary>
/// <param name="Sync">What raising the interface produced.</param>
/// <param name="WasGone">Whether the host had lost the interface, not only brought it down.</param>
/// <param name="Clients">How many clients that are on went back on the interface.</param>
public sealed record EndpointMended(EndpointSync Sync, bool WasGone, int Clients);

/// <summary>
/// Raises the endpoints together with their clients and lays again the interfaces the host lost or brought down, one
/// change of the endpoints at a time.
/// </summary>
public sealed class EndpointKeeper : IDisposable
{
    private readonly IHostNetwork _network;

    private readonly EndpointHost _endpoints;

    private readonly ClientHost _clients;

    private readonly HostTurn _turn = new();

    /// <summary>
    /// ctor
    /// </summary>
    public EndpointKeeper(IHostNetwork network, EndpointHost endpoints, ClientHost clients)
    {
        _network = network;
        _endpoints = endpoints;
        _clients = clients;
    }

    /// <summary>
    /// Waits for the change of the endpoints that goes on and holds them until the hold is let go.
    /// </summary>
    public Task<IDisposable> HoldAsync(CancellationToken ct) => _turn.HoldAsync(ct);

    /// <summary>
    /// Puts one endpoint on its interface and, when it is on and the host took it, its clients on top.
    /// </summary>
    public async Task<EndpointSync> RaiseAsync(ServerConfig config, IReadOnlyList<TunnelClient> clients, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        var sync = await _endpoints.ApplyAsync(config, ct).ConfigureAwait(false);
        if (sync.IsDone && config.IsEnabled)
        {
            await _clients.SyncAsync(config, Of(config, clients), [], ct).ConfigureAwait(false);
        }

        return sync;
    }

    /// <summary>
    /// Puts every endpoint on the host with its clients and lays the rules that carry them.
    /// </summary>
    public async Task<IReadOnlyList<EndpointSync>> SyncAsync(
        IReadOnlyList<ServerConfig> configs,
        IReadOnlyList<TunnelClient> clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(clients);

        var done = new List<EndpointSync>(configs.Count + 1);
        foreach (var config in configs)
        {
            done.Add(await RaiseAsync(config, clients, ct).ConfigureAwait(false));
        }

        done.Add(await _endpoints.FirewallAsync(configs, clients, ct).ConfigureAwait(false));

        return done;
    }

    /// <summary>
    /// Returns the endpoints that are on and whose interface the host lost or carries down.
    /// </summary>
    public IReadOnlyList<ServerConfig> Lost(IReadOnlyList<ServerConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        return [.. configs.Where(config => config.IsEnabled && !_network.IsUp(config.Name))];
    }

    /// <summary>
    /// Lays the interfaces of lost endpoints again: a gone one with its keys, addresses and clients, a downed one is
    /// brought up with what it carries.
    /// </summary>
    public async Task<IReadOnlyList<EndpointMended>> MendAsync(
        IReadOnlyList<ServerConfig> lost,
        IReadOnlyList<TunnelClient> clients,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lost);
        ArgumentNullException.ThrowIfNull(clients);

        var mended = new List<EndpointMended>(lost.Count);
        foreach (var config in lost)
        {
            if (_network.HasLink(config.Name))
            {
                mended.Add(new EndpointMended(await _endpoints.ApplyAsync(config, ct).ConfigureAwait(false), false, 0));

                continue;
            }

            var own = Of(config, clients);
            var sync = await RaiseAsync(config, own, ct).ConfigureAwait(false);
            mended.Add(new EndpointMended(sync, true, sync.IsDone ? own.Count(client => client.IsEnabled) : 0));
        }

        return mended;
    }

    /// <summary>
    /// Lets the gate go.
    /// </summary>
    public void Dispose() => _turn.Dispose();

    private static TunnelClient[] Of(ServerConfig config, IReadOnlyList<TunnelClient> clients) =>
        [.. clients.Where(client => client.ConfigId == config.Id)];
}
