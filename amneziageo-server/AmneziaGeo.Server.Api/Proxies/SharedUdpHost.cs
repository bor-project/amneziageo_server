using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Udp;
using AmneziaGeo.Server.Routing.Guard;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Coordinates shared VPN and QUIC UDP listeners.
/// </summary>
public sealed class SharedUdpHost(UdpPorts ports, IHostNetwork network, IAwgDevices devices, ILogger<SharedUdpHost> logger, string? directory = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, UdpFront> _fronts = [];
    private readonly Dictionary<int, UdpBackend[]> _backends = [];
    private volatile UdpFront[] _visible = [];
    private string _stamp = "";
    private IReadOnlyDictionary<string, string> _remembered = new Dictionary<string, string>();
    private readonly CancellationTokenSource _stopping = new();
    private Task _writing = Task.CompletedTask;
    private bool _loaded;
    private volatile GuardHold[] _blocked = [];

    public IReadOnlyList<UdpFrontCounters> Snapshot() => _visible.Select(one => one.Snapshot()).OrderBy(one => one.Port).ToArray();

    public void Block(IReadOnlyList<GuardHold> holds)
    {
        _blocked = [.. holds];
        foreach (var front in _visible)
        {
            front.Block(holds.Select(one => ((int)one.Port, one.Source)));
        }
    }

    public IPEndPoint KernelEndpoint(string name, IPEndPoint endpoint)
    {
        _gate.Wait();
        try
        {
            if (Resolve(endpoint) is not null)
            {
                return endpoint;
            }

            var front = _visible.FirstOrDefault(one => one.HasBackend("vpn:" + name));
            return front?.Relay("vpn:" + name, endpoint) ?? endpoint;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PreflightAsync(IReadOnlyList<ServerConfig> configs, IReadOnlySet<int> quic, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                await LoadMappingsAsync(ct).ConfigureAwait(false);
                _loaded = true;
            }

            var live = configs.Where(one => one.IsEnabled).ToArray();
            if (live.GroupBy(one => (one.ListenPort, Key: Curve25519.PublicOf(one.PrivateKey))).Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException("udp-identity-taken");
            }

            foreach (var port in live.Select(one => one.ListenPort).Concat(quic).Distinct())
            {
                if (_fronts.ContainsKey(port) || live.Any(one => devices.Find(one.Name)?.ListenPort == port))
                {
                    continue;
                }

                using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies public UDP listeners and private backend ports.
    /// </summary>
    public async Task PrepareAsync(IReadOnlyList<ServerConfig> configs, IReadOnlySet<int> quic, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                await LoadMappingsAsync(ct).ConfigureAwait(false);
                _loaded = true;
            }

            var live = configs.Where(one => one.IsEnabled).ToArray();
            var stamp = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Configs = live.OrderBy(one => one.Name).ToArray(), Quic = quic.Order().ToArray(),
            })));
            if (_stamp == stamp && _fronts.Values.All(one => one.IsRunning))
            {
                return;
            }

            var shared = live.GroupBy(one => one.ListenPort).Where(group => group.Count() > 1 || quic.Contains(group.Key) || _fronts.ContainsKey(group.Key))
                .Select(group => group.Key).Concat(quic).ToHashSet();
            var vpn = live.Where(one => shared.Contains(one.ListenPort)).ToArray();
            if (vpn.GroupBy(one => (one.ListenPort, Key: Curve25519.PublicOf(one.PrivateKey))).Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException("udp-identity-taken");
            }

            var previousVpn = ports.Vpn;
            var previousQuic = ports.Quic;
            var used = live.Select(one => one.ListenPort).Concat(quic).Concat(previousVpn.Values).Concat(previousQuic.Values).ToHashSet();
            var wantedVpn = vpn.ToDictionary(one => one.Name, one => previousVpn.GetValueOrDefault(one.Name) is > 0 and var held ? held : Free(used));
            var wantedQuic = quic.ToDictionary(one => one, one => previousQuic.GetValueOrDefault(one) is > 0 and var held ? held : Free(used));
            var backends = shared.ToDictionary(port => port, port => vpn.Where(one => one.ListenPort == port)
                .Select(one => new UdpBackend("vpn:" + one.Name, wantedVpn[one.Name], new UdpPacketReader(one), one.UdpPrivate))
                .Concat(quic.Contains(port) ? [new UdpBackend("quic", wantedQuic[port])] : []).ToArray());
            var moved = new List<(ServerConfig Config, ushort Port, string Guard)>();
            var created = new List<int>();
            var migrated = new List<(string Name, AwgPeer Peer)>();
            try
            {
                ports.Set(wantedVpn, wantedQuic);
                foreach (var config in vpn)
                {
                    var port = wantedVpn[config.Name];
                    var current = devices.Find(config.Name);
                    if (current is null || current.ListenPort == port)
                    {
                        continue;
                    }

                    var guard = await network.ReadFirewallAsync(UdpPrivacyRuleset.Table(config.Name), ct).ConfigureAwait(false);
                    moved.Add((config, current.ListenPort, guard));
                    await network.FirewallAsync(UdpPrivacyRuleset.Text(config.Name, port), ct).ConfigureAwait(false);
                    devices.Apply(new AwgUpdate { Name = config.Name, ListenPort = (ushort)port });
                }

                foreach (var (port, targets) in backends)
                {
                    if (_fronts.TryGetValue(port, out var failed) && !failed.IsRunning)
                    {
                        _remembered = _remembered.Concat(failed.Mappings).GroupBy(one => one.Key)
                            .ToDictionary(group => group.Key, group => group.Last().Value);
                        _fronts.Remove(port);
                        await failed.DisposeAsync().ConfigureAwait(false);
                    }

                    if (!_fronts.TryGetValue(port, out var front))
                    {
                        front = new UdpFront(port, logger);
                        _fronts[port] = front;
                        created.Add(port);
                    }

                    front.Configure(targets);
                    front.Block(_blocked.Select(one => ((int)one.Port, one.Source)));
                }

                foreach (var config in vpn)
                {
                    var current = devices.Find(config.Name);
                    if (current is null)
                    {
                        continue;
                    }

                    foreach (var peer in current.Peers.Where(one => one.Endpoint is not null))
                    {
                        var client = Resolve(peer.Endpoint!, config.Name) ?? peer.Endpoint!;
                        var relay = _fronts[config.ListenPort].Relay("vpn:" + config.Name, client);
                        if (relay.Equals(peer.Endpoint))
                        {
                            continue;
                        }

                        migrated.Add((config.Name, peer));
                        devices.Apply(new AwgUpdate { Name = config.Name, Peers = [new AwgPeerUpdate
                        {
                            PublicKey = peer.PublicKey, Endpoint = relay, UpdateOnly = true,
                        }] });
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The shared UDP plan was rolled back");
                ports.Set(previousVpn, previousQuic);
                foreach (var port in created)
                {
                    if (_fronts.Remove(port, out var front))
                    {
                        await front.DisposeAsync().ConfigureAwait(false);
                    }
                }

                foreach (var (port, targets) in _backends)
                {
                    if (_fronts.TryGetValue(port, out var front))
                    {
                        front.Configure(targets);
                    }
                }

                foreach (var (config, port, guard) in moved.AsEnumerable().Reverse())
                {
                    try
                    {
                        devices.Apply(new AwgUpdate { Name = config.Name, ListenPort = port });
                        await network.FirewallAsync(guard.Length == 0 ? UdpPrivacyRuleset.Clear(config.Name)
                            : UdpPrivacyRuleset.Text(config.Name, port), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollback)
                    {
                        logger.LogError(rollback, "The UDP listener of {Interface} could not be restored", config.Name);
                    }
                }

                foreach (var (name, peer) in migrated)
                {
                    try
                    {
                        devices.Apply(new AwgUpdate { Name = name, Peers = [new AwgPeerUpdate
                        {
                            PublicKey = peer.PublicKey, Endpoint = peer.Endpoint, UpdateOnly = true,
                        }] });
                    }
                    catch (Exception rollback)
                    {
                        logger.LogError(rollback, "A UDP peer of {Interface} could not be restored", name);
                    }
                }

                throw;
            }

            foreach (var port in _fronts.Keys.Where(one => !shared.Contains(one)).ToArray())
            {
                var front = _fronts[port];
                _fronts.Remove(port);
                await front.DisposeAsync().ConfigureAwait(false);
            }

            _backends.Clear();
            foreach (var (port, targets) in backends)
            {
                _backends[port] = targets;
            }
            _visible = [.. _fronts.Values];
            ports.SetResolver(endpoint => Resolve(endpoint));
            _stamp = stamp;
            if (directory is not null && _writing.IsCompleted)
            {
                _writing = WatchMappingsAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resolves a backend endpoint to its public peer.
    /// </summary>
    public IPEndPoint? Resolve(IPEndPoint endpoint, string? backend = null)
    {
        var current = _visible.Select(one => one.Client(endpoint)).FirstOrDefault(one => one is not null);
        return current ?? (backend is not null && (_remembered.TryGetValue("vpn:" + backend + "|" + endpoint, out var text)
                || _remembered.TryGetValue(backend + "|" + endpoint, out text))
            && IPEndPoint.TryParse(text, out var original) ? original : null);
    }

    private async Task LoadMappingsAsync(CancellationToken ct)
    {
        if (directory is null || !File.Exists(Path.Combine(directory, "peers.json")))
        {
            return;
        }

        try
        {
            var file = Path.Combine(directory, "peers.json");
            if (new FileInfo(file).Length > 4_194_304)
            {
                throw new InvalidDataException("The saved UDP peer mappings are too large.");
            }

            var saved = JsonSerializer.Deserialize<SavedRelays>(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false), ProxyCatalog.Json);
            if (saved is { Version: 1, Mappings: not null } && saved.Created <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - saved.Created < TimeSpan.FromMinutes(10)
                && saved.Mappings.Count <= 16384)
            {
                _remembered = saved.Mappings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "The saved UDP peer mappings could not be read");
        }
    }

    private async Task WatchMappingsAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var revision = "";
        var written = DateTimeOffset.MinValue;
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false))
            {
                var current = string.Join(',', _visible.Select(one => one.Port + ":" + one.Revision));
                if (current != revision || DateTimeOffset.UtcNow - written > TimeSpan.FromMinutes(1))
                {
                    await WriteMappingsAsync(_stopping.Token).ConfigureAwait(false);
                    revision = current;
                    written = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task WriteMappingsAsync(CancellationToken ct)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            var mappings = _visible.SelectMany(one => one.Mappings).Take(16384).ToDictionary(one => one.Key, one => one.Value);
            await ProxyCatalog.WriteAsync(Path.Combine(directory, "peers.json"), new SavedRelays(1, DateTimeOffset.UtcNow, mappings), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The UDP peer mappings could not be saved");
        }
    }

    private sealed record SavedRelays(int Version, DateTimeOffset Created, Dictionary<string, string> Mappings);

    private static int Free(HashSet<int> used)
    {
        for (var attempt = 0; attempt < 128; attempt++)
        {
            using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
            var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            if (used.Add(port))
            {
                return port;
            }
        }

        throw new IOException("udp-backend-port-unavailable");
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            await _writing.ConfigureAwait(false);
            await WriteMappingsAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var front in _fronts.Values)
            {
                await front.DisposeAsync().ConfigureAwait(false);
            }

            _fronts.Clear();
            _visible = [];
            ports.Set(new Dictionary<string, int>(), new Dictionary<int, int>());
            ports.SetResolver(null);
            _stopping.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }
}
