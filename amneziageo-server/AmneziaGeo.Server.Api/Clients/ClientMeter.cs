using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
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
/// Counts client traffic and applies limits.
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

    private readonly DatabaseVersion? _version;

    private readonly HashSet<long> _spent = [];

    private readonly HashSet<long> _pending = [];

    private HashSet<long> _exhausted = [];

    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly SemaphoreSlim _periodWrite = new(1, 1);

    private readonly ClientBandwidthHost? _bandwidth;

    private readonly Dictionary<long, string> _generations = [];

    private static readonly string Boot = File.Exists("/proc/sys/kernel/random/boot_id")
        ? File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim() : Guid.NewGuid().ToString("N");

    private IReadOnlyList<ClientTemplate> _templates = [];

    private readonly ConcurrentDictionary<string, uint> _indexes = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ShapeStamp> _shaped = new(StringComparer.Ordinal);

    private IReadOnlyList<ServerConfig> _endpoints = [];

    private IReadOnlyList<TunnelClient> _clients = [];

    private long? _seen;

    private DateTimeOffset _written;

    private bool _loaded;

    private bool _blind;

    private bool _writePending;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientMeter(
        IServiceScopeFactory scopes,
        IAwgDevices devices,
        ClientHost host,
        TrafficLedger ledger,
        TimeProvider time,
        ILogger<ClientMeter> logger,
        DatabaseVersion? version = null,
        ClientBandwidthHost? bandwidth = null)
    {
        _scopes = scopes;
        _devices = devices;
        _host = host;
        _ledger = ledger;
        _time = time;
        _logger = logger;
        _version = version;
        _bandwidth = bandwidth;
        _host.PrepareLimits = PrepareAsync;
    }

    /// <summary>
    /// Reads the counters once, takes off the clients that used up their limit and lays back the ones let in again.
    /// </summary>
    public async Task MeasureAsync(CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MeasureCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopes.CreateScope();
            await LoadAsync(scope.ServiceProvider, _time.GetUtcNow(), ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task LoadAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        if (!_loaded)
        {
            _ledger.Load(await services.GetRequiredService<TrafficStore>().LatestAsync(ct).ConfigureAwait(false));
            _ledger.LoadPeriods(await services.GetRequiredService<TrafficPeriodStore>().ReadAsync(ct).ConfigureAwait(false));
            _loaded = true;
            _written = now;
        }
        var version = _version?.Read();
        if (version is null || version != _seen)
        {
            _endpoints = await services.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
            _clients = await services.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
            _templates = await services.GetRequiredService<TemplateStore>().ListAsync(ct).ConfigureAwait(false);
            _ledger.SetLimits(_clients, _templates);
            _seen = version;
        }
    }

    private async Task MeasureCoreAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var now = _time.GetUtcNow();
        await LoadAsync(services, now, ct).ConfigureAwait(false);

        var day = _ledger.HostDay();
        if (day != _ledger.Day)
        {
            await WriteAsync(services, now, ct).ConfigureAwait(false);
            _ledger.Turn(day);
        }


        ObserveClients(now);
        var exhausted = _clients.Where(client => _ledger.Of(client).Limits?.IsExhausted == true)
            .Select(client => client.Id).ToHashSet();
        try
        {
            if (_writePending || !exhausted.SetEquals(_exhausted) || _clients.Any(client => _ledger.IsSpent(client) && !_spent.Contains(client.Id)))
            {
                await WriteAsync(services, now, ct).ConfigureAwait(false);
                _exhausted = exhausted;
            }
        }
        finally
        {
            await ShapeAsync(ct).ConfigureAwait(false);
            await SettleAsync(_endpoints, _clients, ct).ConfigureAwait(false);
        }
        if (now - _written >= WriteEvery || _clients.Any(client =>
            _ledger.Of(client).Limits is { Policy.QuotaBytes: > 0 }
            || (_ledger.Of(client).Limits is null && client.DailyLimit > 0)))
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

    private void ObserveClients(DateTimeOffset now)
    {
        lock (_ledger.CounterGate)
        {
            var readings = Read(_endpoints, _clients);
            _ledger.Observe(_clients, readings, now, _generations);
        }
    }

    private List<TrafficReading> Read(IReadOnlyList<ServerConfig> endpoints, IReadOnlyList<TunnelClient> clients)
    {
        var readings = new List<TrafficReading>();
        _indexes.Clear();
        foreach (var endpoint in endpoints.Where(one => one.IsEnabled))
        {
            var mine = new Dictionary<string, TunnelClient>(StringComparer.Ordinal);
            foreach (var client in clients.Where(one => one.ConfigId == endpoint.Id))
            {
                mine.TryAdd(client.PublicKey, client);
            }

            if (mine.Count == 0 || Find(endpoint.Name) is not { } device)
            {
                continue;
            }

            _indexes[endpoint.Name] = device.Index;
            foreach (var client in mine.Values.Where(client => !device.Peers.Any(peer => peer.PublicKey == client.PublicKey)))
            {
                _ledger.ForgetCounters(client.Id);
            }

            foreach (var peer in device.Peers)
            {
                if (mine.TryGetValue(peer.PublicKey, out var client))
                {
                    readings.Add(new TrafficReading(client.Id, peer.RxBytes, peer.TxBytes));
                    _generations[client.Id] = Generation(device.Index, client.PublicKey);
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

    private async Task ShapeAsync(CancellationToken ct)
    {
        foreach (var endpoint in _endpoints)
        {
            var mine = _clients.Where(client => client.ConfigId == endpoint.Id).ToArray();
            await ShapeOneAsync(endpoint, mine, _indexes.GetValueOrDefault(endpoint.Name), false, ct).ConfigureAwait(false);
        }

        foreach (var name in _shaped.Keys.Where(name => !_endpoints.Any(endpoint => endpoint.Name == name)))
        {
            if (_bandwidth is null || (await _bandwidth.ApplyAsync(name, [], ct).ConfigureAwait(false)).IsDone)
            {
                _shaped.TryRemove(name, out _);
            }
        }
    }

    private async Task<BandwidthSync> PrepareAsync(ServerConfig endpoint, IReadOnlyList<TunnelClient> mine, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var clients = await services.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
            var templates = await services.GetRequiredService<TemplateStore>().ListAsync(ct).ConfigureAwait(false);
            _ledger.SetLimits(clients, templates);
            var index = 0u;
            lock (_ledger.CounterGate)
            {
                var device = Find(endpoint.Name);
                index = device?.Index ?? 0;
                foreach (var client in mine)
                {
                    var peer = device?.Peers.FirstOrDefault(peer => peer.PublicKey == client.PublicKey);
                    var seen = peer is null ? default : new ClientUsage(peer.RxBytes, peer.TxBytes);
                    _ledger.Prime(client, seen, Generation(index, client.PublicKey));
                    if (peer is null)
                    {
                        _ledger.ForgetCounters(client.Id);
                    }
                }
            }

            await WritePeriodsAsync(services, ct).ConfigureAwait(false);
            return await ShapeOneAsync(endpoint, mine, index, true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "the traffic policy of {Interface} was not prepared", endpoint.Name);
            _ledger.Applied(mine, false, ex.Message);
            return new BandwidthSync(false, ex.Message);
        }
    }

    private async Task<BandwidthSync> ShapeOneAsync(
        ServerConfig endpoint, IReadOnlyList<TunnelClient> mine, uint index, bool force, CancellationToken ct)
    {
        var shaped = new List<ClientBandwidth>();
        foreach (var client in mine.Where(client => endpoint.IsEnabled && client.IsEnabled))
        {
            if (_ledger.Of(client).Limits is { } limits
                && (limits.UploadBitsPerSecond > 0 || limits.DownloadBitsPerSecond > 0))
            {
                shaped.Add(new ClientBandwidth(client.Id, client.Address,
                    limits.UploadBitsPerSecond, limits.DownloadBitsPerSecond));
            }
        }

        var signature = System.Text.Json.JsonSerializer.Serialize(shaped) + ":" + index;
        var now = _time.GetUtcNow();
        if (!force && _shaped.TryGetValue(endpoint.Name, out var stamp) && stamp.Signature == signature
            && stamp.Result.IsDone && now - stamp.At < TimeSpan.FromSeconds(30))
        {
            _ledger.Applied(mine, true, string.Empty);
            return stamp.Result;
        }

        var sync = _bandwidth is null ? new BandwidthSync(true, string.Empty)
            : await _bandwidth.ApplyAsync(endpoint.Name, shaped, ct).ConfigureAwait(false);
        _shaped[endpoint.Name] = new ShapeStamp(signature, now, sync);
        _ledger.Applied(mine, sync.IsDone, sync.Message);
        return sync;
    }

    private static string Generation(uint index, string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Boot}:{index}:{key}")));

    private sealed record ShapeStamp(string Signature, DateTimeOffset At, BandwidthSync Result);

    private async Task SettleAsync(IReadOnlyList<ServerConfig> endpoints, IReadOnlyList<TunnelClient> clients, CancellationToken ct)
    {
        var spent = clients.Where(one => one.IsEnabled && _ledger.IsSpent(one)).Select(one => one.Id).ToHashSet();
        var moved = clients.Where(one => spent.Contains(one.Id) != _spent.Contains(one.Id)).ToArray();
        _spent.RemoveWhere(id => !clients.Any(client => client.Id == id));
        _pending.RemoveWhere(id => !endpoints.Any(endpoint => endpoint.Id == id));
        foreach (var endpoint in endpoints.Where(one => _pending.Contains(one.Id) || moved.Any(client => client.ConfigId == one.Id)))
        {
            var mine = clients.Where(client => client.ConfigId == endpoint.Id).ToArray();
            var sync = await _host.SyncAsync(endpoint, mine, [], ct).ConfigureAwait(false);
            if (!sync.IsDone)
            {
                _pending.Add(endpoint.Id);
                _logger.LogWarning("the clients of {Endpoint} were not laid anew: {Reason}", endpoint.Name, sync.Message);
                continue;
            }

            _pending.Remove(endpoint.Id);
            foreach (var client in mine)
            {
                if (spent.Contains(client.Id))
                {
                    _spent.Add(client.Id);
                }
                else
                {
                    _spent.Remove(client.Id);
                }
            }
        }
    }

    public async Task<bool> ResetAsync(long clientId, CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MeasureCoreAsync(ct).ConfigureAwait(false);
            var client = _clients.FirstOrDefault(client => client.Id == clientId);
            if (client is null || _ledger.ResetPeriod(client) is not { } row)
            {
                return false;
            }

            using var scope = _scopes.CreateScope();
            await _periodWrite.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await scope.ServiceProvider.GetRequiredService<TrafficPeriodStore>().SaveAsync([row], ct).ConfigureAwait(false);
                _ledger.AcceptReset(row);
            }
            finally
            {
                _periodWrite.Release();
            }
            await ShapeAsync(ct).ConfigureAwait(false);
            await SettleAsync(_endpoints, _clients, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _turn.Release();
        }
    }

    private async Task WritePeriodsAsync(IServiceProvider services, CancellationToken ct)
    {
        await _periodWrite.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = _ledger.PendingPeriods();
            await services.GetRequiredService<TrafficPeriodStore>().SaveAsync(rows, ct).ConfigureAwait(false);
            _ledger.WrittenPeriods(rows);
        }
        finally
        {
            _periodWrite.Release();
        }
    }

    private async Task WriteAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        var saved = false;
        try
        {
            var days = _ledger.Pending();
            if (days.Count > 0)
            {
                await services.GetRequiredService<TrafficStore>().SaveAsync(days, ct).ConfigureAwait(false);
                _ledger.Written(days);
            }

            await WritePeriodsAsync(services, ct).ConfigureAwait(false);

            _written = now;

            saved = true;
        }
        finally
        {
            _writePending = !saved;
        }
    }
}
