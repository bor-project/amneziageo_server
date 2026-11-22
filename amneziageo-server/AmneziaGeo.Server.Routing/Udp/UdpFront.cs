using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using AmneziaGeo.Server.Routing.Names;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Routing.Udp;

public sealed record UdpBackend(string Id, int Port, UdpPacketReader? Reader = null, bool LocalOnly = false);
public sealed record UdpBackendCounters(string Name, long Forwarded, long Replies);
public sealed record UdpFrontCounters(int Port, bool Running, int Flows, long Received, long Dropped, IReadOnlyList<UdpBackendCounters> Backends);

/// <summary>
/// Relays a public UDP listener to protocol backends.
/// </summary>
public sealed class UdpFront : IAsyncDisposable
{
    public const int MaximumFlows = 1024;
    private readonly Socket _socket;
    private readonly int _port;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<(string Backend, IPEndPoint Client), Flow> _flows = new();
    private readonly ConcurrentDictionary<uint, ConcurrentDictionary<string, long>> _indices = new();
    private readonly Task _receive;
    private readonly Task _sweep;
    private readonly object _creating = new();
    private volatile UdpBackend[] _backends = [];
    private long _revision;
    private long _received;
    private long _dropped;
    private readonly ConcurrentDictionary<string, Counters> _counts = new();
    private volatile HashSet<(int Port, IPEndPoint Source)> _blocked = [];

    /// <summary>
    /// ctor
    /// </summary>
    public UdpFront(int port, ILogger logger)
    {
        _logger = logger;
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true, ReceiveBufferSize = 1_048_576, SendBufferSize = 262_144 };
        try
        {
            _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            _port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        _receive = ReceiveAsync();
        _sweep = SweepAsync();
    }

    public int Port => _port;
    public int FlowCount => _flows.Count;
    public bool IsRunning => !_receive.IsCompleted;
    public long Revision => Interlocked.Read(ref _revision);
    public IReadOnlyDictionary<string, string> Mappings => _flows.Values.Where(one => one.Backend.Reader is not null)
        .ToDictionary(one => one.Backend.Id + "|" + one.Relay, one => one.Client.ToString());
    public UdpFrontCounters Snapshot() => new(Port, IsRunning, FlowCount, Interlocked.Read(ref _received), Interlocked.Read(ref _dropped),
        _counts.Select(one => new UdpBackendCounters(one.Key, Interlocked.Read(ref one.Value.Forwarded), Interlocked.Read(ref one.Value.Replies))).ToArray());
    public bool HasBackend(string name) => _backends.Any(one => one.Id == name);
    public void Block(IEnumerable<(int Port, IPEndPoint Source)> blocked) => _blocked = blocked.ToHashSet();

    /// <summary>
    /// Replaces protocol destinations.
    /// </summary>
    public void Configure(IReadOnlyList<UdpBackend> backends)
    {
        if (backends.Count(one => one.Reader is null) > 1 || backends.Select(one => one.Id).Distinct().Count() != backends.Count
            || backends.Any(one => one.Port is < 1 or > 65535))
        {
            throw new ArgumentException("The UDP backend identities or ports are invalid.", nameof(backends));
        }

        _backends = [.. backends];
        foreach (var (key, flow) in _flows)
        {
            var current = backends.FirstOrDefault(one => one.Id == flow.Backend.Id && one.Port == flow.Backend.Port);
            if (current is null)
            {
                Remove(key, flow);
            }
            else
            {
                flow.Backend = current;
            }
        }
    }

    /// <summary>
    /// Returns a loopback relay for an existing peer.
    /// </summary>
    public IPEndPoint Relay(string backend, IPEndPoint client)
    {
        var target = _backends.Single(one => one.Id == backend);
        var flow = Get(target, Normalize(Copy(client))) ?? throw new IOException("The shared UDP listener has too many clients.");
        return Copy(flow.Relay);
    }

    /// <summary>
    /// Returns the public peer endpoint of a relay.
    /// </summary>
    public IPEndPoint? Client(IPEndPoint relay)
    {
        var client = _flows.Values.FirstOrDefault(one => one.Relay.Equals(relay))?.Client;
        return client is null ? null : Copy(client);
    }

    private async Task ReceiveAsync()
    {
        await Task.Yield();
        var buffer = new byte[65536];
        try
        {
            var ipv4Loopback = NetworkInterface.LoopbackInterfaceIndex;
            var ipv6Loopback = NetworkInterface.IPv6LoopbackInterfaceIndex;
            while (!_stop.IsCancellationRequested)
            {
                var result = await _socket.ReceiveMessageFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.IPv6Any, 0), _stop.Token).ConfigureAwait(false);
                Interlocked.Increment(ref _received);
                var client = Normalize((IPEndPoint)result.RemoteEndPoint);
                var backend = Select(buffer.AsSpan(0, result.ReceivedBytes), client);
                var local = result.PacketInformation.Interface == ipv4Loopback
                    || result.PacketInformation.Interface == ipv6Loopback;
                if (backend is null || backend.LocalOnly && !local || _blocked.Contains((backend.Port, client)))
                {
                    Interlocked.Increment(ref _dropped);
                    continue;
                }

                var flow = Get(backend, client);
                if (flow is null)
                {
                    Interlocked.Increment(ref _dropped);
                    continue;
                }

                flow.Touched = Environment.TickCount64;
                try
                {
                    await flow.Socket.SendAsync(buffer.AsMemory(0, result.ReceivedBytes), SocketFlags.None, _stop.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref _counts.GetOrAdd(backend.Id, _ => new()).Forwarded);
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "The shared UDP backend did not take a packet");
                    Remove((backend.Id, client), flow);
                }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The shared UDP listener stopped receiving");
            throw;
        }
    }

    private UdpBackend? Select(ReadOnlySpan<byte> packet, IPEndPoint client)
    {
        var backends = _backends;
        if (backends.Length == 1 && backends[0].Reader is null && _flows.ContainsKey((backends[0].Id, client)))
        {
            return backends[0];
        }

        var chosen = default(UdpBackend);
        foreach (var backend in backends)
        {
            if (backend.Reader is null || !backend.Reader.Handshake(packet))
            {
                continue;
            }

            if (chosen is not null)
            {
                return null;
            }

            chosen = backend;
        }

        if (chosen is not null)
        {
            return chosen;
        }

        foreach (var backend in backends)
        {
            if (backend.Reader?.Receiver(packet) is not { } receiver || !_indices.TryGetValue(receiver, out var owners)
                || !owners.ContainsKey(backend.Id))
            {
                continue;
            }

            if (chosen is not null)
            {
                return null;
            }

            chosen = backend;
            owners[backend.Id] = Environment.TickCount64;
        }

        if (chosen is not null)
        {
            return chosen;
        }

        var quic = backends.SingleOrDefault(one => one.Reader is null);
        if (quic is not null && QuicInitial.IsLong(packet) && QuicInitial.Open(packet) is not null)
        {
            return quic;
        }

        var known = default(UdpBackend);
        var count = 0;
        foreach (var backend in backends)
        {
            if (_flows.ContainsKey((backend.Id, client)))
            {
                known = backend;
                count++;
            }
        }
        if (count == 1 && (known!.Reader is null || known.Reader.Receiver(packet) is not null))
        {
            return known;
        }

        return quic is not null && packet.Length >= 21 && (packet[0] & 0xC0) == 0x40 ? quic : null;
    }

    private Flow? Get(UdpBackend backend, IPEndPoint client)
    {
        var key = (backend.Id, client);
        if (_flows.TryGetValue(key, out var found))
        {
            return found;
        }

        lock (_creating)
        {
            if (_flows.TryGetValue(key, out found))
            {
                return found;
            }

            if (_flows.Count >= MaximumFlows)
            {
                return null;
            }

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = 1_048_576, SendBufferSize = 262_144 };
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                socket.Connect(new IPEndPoint(IPAddress.Loopback, backend.Port));
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            var flow = new Flow(backend, client, socket);
            _flows[key] = flow;
            Interlocked.Increment(ref _revision);
            flow.Replies = RepliesAsync(flow);
            return flow;
        }
    }

    private async Task RepliesAsync(Flow flow)
    {
        var buffer = new byte[65536];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var size = await flow.Socket.ReceiveAsync(buffer, SocketFlags.None, _stop.Token).ConfigureAwait(false);
                Interlocked.Increment(ref _counts.GetOrAdd(flow.Backend.Id, _ => new()).Replies);
                flow.Answered = true;
                flow.Touched = Environment.TickCount64;
                var header = flow.Backend.Reader?.Header(buffer.AsSpan(0, size)) ?? flow.Backend.Reader?.Header(buffer.AsSpan(0, size), true);
                if (header is { Kind: 1 or 2, Sender: not 0 })
                {
                    _indices.GetOrAdd(header.Value.Sender, _ => new())[flow.Backend.Id] = flow.Touched;
                }

                await _socket.SendToAsync(buffer.AsMemory(0, size), SocketFlags.None, flow.Destination, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            if (!_stop.IsCancellationRequested && ex is SocketException { SocketErrorCode: not SocketError.OperationAborted })
            {
                _logger.LogDebug(ex, "A shared UDP relay stopped");
            }
        }
        finally
        {
            Remove((flow.Backend.Id, flow.Client), flow);
        }
    }

    private async Task SweepAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                var now = Environment.TickCount64;
                foreach (var (key, flow) in _flows)
                {
                    if (now - flow.Touched > (flow.Answered ? 300_000 : 30_000))
                    {
                        Remove(key, flow);
                    }
                }

                foreach (var (index, owners) in _indices)
                {
                    foreach (var (owner, touched) in owners)
                    {
                        if (now - touched > 300_000)
                        {
                            owners.TryRemove(owner, out _);
                        }
                    }

                    if (owners.IsEmpty)
                    {
                        _indices.TryRemove(new KeyValuePair<uint, ConcurrentDictionary<string, long>>(index, owners));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private void Remove((string Backend, IPEndPoint Client) key, Flow flow)
    {
        if (_flows.TryRemove(new KeyValuePair<(string, IPEndPoint), Flow>(key, flow)))
        {
            flow.Socket.Dispose();
            Interlocked.Increment(ref _revision);
        }
    }

    private static IPEndPoint Normalize(IPEndPoint client) => client.Address.IsIPv4MappedToIPv6
        ? new IPEndPoint(client.Address.MapToIPv4(), client.Port) : client;

    private static IPEndPoint Copy(IPEndPoint endpoint) => new(endpoint.AddressFamily == AddressFamily.InterNetworkV6
        ? new IPAddress(endpoint.Address.GetAddressBytes(), endpoint.Address.ScopeId) : new IPAddress(endpoint.Address.GetAddressBytes()), endpoint.Port);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _socket.Dispose();
        var replies = _flows.Values.Select(one => one.Replies).ToArray();
        foreach (var (key, flow) in _flows)
        {
            Remove(key, flow);
        }

        try
        {
            await Task.WhenAll(replies.Append(_receive).Append(_sweep)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The stopped UDP listener completed with an error");
        }
        _stop.Dispose();
    }

    private sealed class Flow(UdpBackend backend, IPEndPoint client, Socket socket)
    {
        public UdpBackend Backend { get; set; } = backend;
        public IPEndPoint Client { get; } = client;
        public IPEndPoint Destination { get; } = new(client.AddressFamily == AddressFamily.InterNetwork
            ? client.Address.MapToIPv6() : client.Address, client.Port);
        public Socket Socket { get; } = socket;
        public IPEndPoint Relay { get; } = (IPEndPoint)socket.LocalEndPoint!;
        public long Touched = Environment.TickCount64;
        public bool Answered;
        public Task Replies = Task.CompletedTask;
    }

    private sealed class Counters
    {
        public long Forwarded;
        public long Replies;
    }
}
