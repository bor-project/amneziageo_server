using System.Data.Common;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Counts the traffic of the clients and keeps a client off its interface for the rest of the day it used up its limit.
/// </summary>
public sealed class ClientMeter : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan WriteEvery = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;

    private readonly IAwgDevices _devices;

    private readonly ClientHost _host;

    private readonly TrafficLedger _ledger;

    private readonly TimeProvider _time;

    private readonly ILogger<ClientMeter> _logger;

    private readonly HashSet<long> _spent = [];

    private DateTimeOffset _written;

    private bool _loaded;

    private bool _blind;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientMeter(
        IServiceScopeFactory scopes,
        IAwgDevices devices,
        ClientHost host,
        TrafficLedger ledger,
        TimeProvider time,
        ILogger<ClientMeter> logger)
    {
        _scopes = scopes;
        _devices = devices;
        _host = host;
        _ledger = ledger;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Reads the counters once, takes off the clients that used up their limit and lays back the ones let in again.
    /// </summary>
    public async Task MeasureAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var now = _time.GetUtcNow();
        if (!_loaded)
        {
            _ledger.Load(await services.GetRequiredService<TrafficStore>().LatestAsync(ct).ConfigureAwait(false));
            _loaded = true;
            _written = now;
        }

        var day = _ledger.HostDay();
        if (day != _ledger.Day)
        {
            await WriteAsync(services, now, ct).ConfigureAwait(false);
            _ledger.Turn(day);
        }

        var endpoints = await services.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        var clients = await services.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
        _ledger.Observe(clients, Read(endpoints, clients), now);
        await SettleAsync(endpoints, clients, ct).ConfigureAwait(false);
        if (now - _written >= WriteEvery)
        {
            await WriteAsync(services, now, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!_loaded)
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            await WriteAsync(scope.ServiceProvider, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or DbException or DbUpdateException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "the traffic of the clients was not written");
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Tick, _time);
            do
            {
                try
                {
                    await MeasureAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or DbException or DbUpdateException)
                {
                    _logger.LogWarning(ex, "the traffic of the clients was not counted");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private List<TrafficReading> Read(IReadOnlyList<ServerConfig> endpoints, IReadOnlyList<TunnelClient> clients)
    {
        var readings = new List<TrafficReading>();
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            if (Find(endpoint.Name) is not { } device)
            {
                continue;
            }

            var mine = new Dictionary<string, TunnelClient>(StringComparer.Ordinal);
            foreach (var client in clients.Where(one => one.ConfigId == endpoint.Id))
            {
                mine.TryAdd(client.PublicKey, client);
            }

            foreach (var peer in device.Peers)
            {
                if (mine.TryGetValue(peer.PublicKey, out var client))
                {
                    readings.Add(new TrafficReading(client.Id, peer.RxBytes, peer.TxBytes));
                }
            }
        }

        return readings;
    }

    private AwgDevice? Find(string name)
    {
        try
        {
            return _devices.Find(name);
        }
        catch (NetlinkException ex)
        {
            if (!_blind)
            {
                _logger.LogWarning(ex, "the meter does not read the interface {Interface}", name);
                _blind = true;
            }

            return null;
        }
    }

    private async Task SettleAsync(IReadOnlyList<ServerConfig> endpoints, IReadOnlyList<TunnelClient> clients, CancellationToken ct)
    {
        var spent = clients.Where(one => one.IsEnabled && _ledger.IsSpent(one)).Select(one => one.Id).ToHashSet();
        var moved = clients.Where(one => spent.Contains(one.Id) != _spent.Contains(one.Id)).ToArray();
        _spent.Clear();
        _spent.UnionWith(spent);
        foreach (var client in moved.Where(one => one.IsEnabled))
        {
            if (spent.Contains(client.Id))
            {
                _logger.LogInformation("the client {Client} used up its daily limit and stays off until the day turns", client.Name);
            }
            else
            {
                _logger.LogInformation("the client {Client} is let back in", client.Name);
            }
        }

        foreach (var endpoint in endpoints.Where(one => moved.Any(client => client.ConfigId == one.Id)))
        {
            var sync = await _host.SyncAsync(endpoint, [.. clients.Where(one => one.ConfigId == endpoint.Id)], [], ct)
                .ConfigureAwait(false);
            if (!sync.IsDone)
            {
                _logger.LogWarning("the clients of {Endpoint} were not laid anew: {Reason}", endpoint.Name, sync.Message);
            }
        }
    }

    private async Task WriteAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        var days = _ledger.Pending();
        if (days.Count > 0)
        {
            await services.GetRequiredService<TrafficStore>().SaveAsync(days, ct).ConfigureAwait(false);
            _ledger.Written(days);
        }

        _written = now;
    }
}
