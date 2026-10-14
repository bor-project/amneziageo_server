using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Names;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Names;

/// <summary>
/// Whether the panel reads the names the connections go to out of the connections themselves.
/// </summary>
public sealed class NameOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Names";

    /// <summary>
    /// Whether the names are read.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether the panel takes the first TCP connections over where the host lets them reach it.
    /// </summary>
    public bool Take { get; set; } = true;

    /// <summary>
    /// The port the panel takes the TCP connections the firewall hands over on.
    /// </summary>
    public int Port { get; set; } = NameDefaults.Port;

    /// <summary>
    /// The most connections the panel carries at once.
    /// </summary>
    public int Most { get; set; } = NameDefaults.MostCarried;
}

/// <summary>
/// Reads the names out of the first packets the firewall shows in its queue: a QUIC connection goes on by the rules
/// its name falls under, a TCP connection that took another way than they name goes on along it while the rules take
/// the next ones, and one a blocking rule takes is cut.
/// </summary>
public sealed class NameQueue : BackgroundService
{
    private const int ReadBuffer = 1 << 18;

    private const int UdpHead = 8;

    private const uint RouteMarks = 0x0000_FFFF;

    private const int MostOnce = 4096;

    private readonly Dictionary<Flow, Held> _held = [];

    private readonly Dictionary<Flow, Said> _said = [];

    private readonly Dictionary<Pair, (DateTimeOffset Until, int Count)> _once = [];

    private readonly ConcurrentQueue<Flow> _ended = new();

    private readonly IServiceScopeFactory _scopes;

    private readonly RoutePlans _plans;

    private readonly NameDesk _desk;

    private readonly NameGate _gate;

    private readonly NameInbounds _inbounds;

    private readonly AccessNames _names;

    private readonly NameOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<NameQueue> _logger;

    private NetfilterQueue? _socket;

    private volatile bool _reading;

    private DateTimeOffset _looked;

    private long _read;

    private long _named;

    private long _reset;

    private long _missed;

    /// <summary>
    /// ctor
    /// </summary>
    public NameQueue(
        IServiceScopeFactory scopes,
        RoutePlans plans,
        NameDesk desk,
        NameGate gate,
        NameInbounds inbounds,
        AccessNames names,
        NameOptions options,
        TimeProvider time,
        ILogger<NameQueue> logger)
    {
        _scopes = scopes;
        _plans = plans;
        _desk = desk;
        _gate = gate;
        _inbounds = inbounds;
        _names = names;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// How many packets the queue handed over.
    /// </summary>
    public long Read => Interlocked.Read(ref _read);

    /// <summary>
    /// How many connections gave their name in the queue.
    /// </summary>
    public long Named => Interlocked.Read(ref _named);

    /// <summary>
    /// How many TCP connections a blocking rule cut.
    /// </summary>
    public long Reset => Interlocked.Read(ref _reset);

    /// <summary>
    /// How many TCP connections stayed on a way the rules do not name, the rules taking the next ones.
    /// </summary>
    public long Missed => Interlocked.Read(ref _missed);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        await Task.Yield();
        try
        {
            _socket = new NetfilterQueue(
                NameDefaults.Queue,
                NameDefaults.Snap,
                NameDefaults.Length,
                NameDefaults.Buffer,
                NameDefaults.Wait);
        }
        catch (Exception ex) when (ex is NetlinkException or DllNotFoundException or EntryPointNotFoundException)
        {
            _gate.CloseQueue($"the names of the connections are not read: {ex.Message}");
            _logger.LogWarning(ex, "the panel could not take the queue of the firewall");

            return;
        }

        await _inbounds.RenewAsync(stopping).ConfigureAwait(false);
        _reading = true;
        var reader = new Thread(Loop) { IsBackground = true, Name = "names of the connections" };
        reader.Start(_socket);
        _gate.OpenQueue();
        _logger.LogInformation("the panel reads the names of the connections of the clients in the queue of the firewall");
        await SettleAsync(stopping).ConfigureAwait(false);
        try
        {
            while (await _desk.SeenAsync(stopping).ConfigureAwait(false))
            {
                await NoteAsync().ConfigureAwait(false);
                await Task.Delay(NameDefaults.Noting, _time, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _gate.CloseQueue();
            await SettleAsync(CancellationToken.None).ConfigureAwait(false);
            _reading = false;
            _socket.Wake();
            reader.Join(NameDefaults.Wait * 10);
            _socket.Dispose();
        }
    }

    // Lays the rules anew, so that they hand over what the panel takes now and nothing else.
    private async Task SettleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "the rules were not laid anew for the queue of the firewall");
        }
    }

    // Sends the addresses looked at to the host.
    private async Task NoteAsync()
    {
        try
        {
            await _desk.NoteAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HostNetworkException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "the addresses looked at did not reach the host");
        }
    }

    // Tells whether no packet and no connection waits in the queue for its time.
    private bool Idle => _held.Count == 0 && _said.Count == 0 && _ended.IsEmpty;

    private void Loop(object? state)
    {
        if (state is not NetfilterQueue socket)
        {
            return;
        }

        var buffer = new byte[ReadBuffer];
        var packets = new List<QueuedPacket>();
        while (_reading)
        {
            try
            {
                if (Idle)
                {
                    socket.Wait();
                }

                var read = socket.Read(buffer);
                if (read > 0)
                {
                    NetfilterQueue.Parse(buffer.AsSpan(0, read), packets);
                    foreach (var packet in packets)
                    {
                        Take(socket, packet);
                    }

                    packets.Clear();
                }

                Expire(socket);
            }
            catch (NetlinkException ex)
            {
                _gate.CloseQueue($"the names of the connections are not read: {ex.Message}");
                _logger.LogWarning(ex, "the panel stopped reading the queue of the firewall");
                socket.Dispose();
                _ = SettleAsync(CancellationToken.None);

                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    // Takes one packet by the protocol it carries.
    private void Take(NetfilterQueue socket, QueuedPacket packet)
    {
        Interlocked.Increment(ref _read);
        var at = AccessFlow.Transport(packet.Payload);
        if (AccessFlow.Read(packet.Payload) is not { } flow || at < 0)
        {
            Back(socket, packet.Id);

            return;
        }

        var key = new Flow(flow.Source, flow.SourcePort, flow.Target, flow.Port);
        if (flow.Protocol == AccessFlow.Tcp)
        {
            Stream(socket, packet, key, at);

            return;
        }

        if (flow.Protocol != AccessFlow.Udp || packet.Payload.Length < at + UdpHead)
        {
            Back(socket, packet.Id);

            return;
        }

        Datagram(socket, packet, key, at);
    }

    // Takes one packet of QUIC: it goes on at once, or waits for the rest of the first message of its connection.
    private void Datagram(NetfilterQueue socket, QueuedPacket packet, Flow key, int at)
    {
        var known = _held.TryGetValue(key, out var held);
        var pieces = QuicInitial.Open(packet.Payload.AsSpan(at + UdpHead));
        if (pieces is null && !known)
        {
            Back(socket, packet.Id);

            return;
        }

        if (held is null)
        {
            if (_held.Count >= NameDefaults.MostHeld)
            {
                Back(socket, packet.Id);

                return;
            }

            held = new Held(_time.GetUtcNow());
            _held[key] = held;
        }

        held.Packets.Add(packet.Id);
        var seen = pieces is null ? SeenName.More : held.Hello.Take(pieces);
        if (seen.Reading == NameReading.More && held.Packets.Count < NameDefaults.MostPackets)
        {
            return;
        }

        _held.Remove(key);
        Finish(socket, key, held, seen.Name);
    }

    // Lets the packets of a QUIC connection go on to be decided anew, the address laid into the sets of the rules its
    // name falls under first.
    private void Finish(NetfilterQueue socket, Flow key, Held held, string name)
    {
        var plan = _plans.Held;
        if (name.Length == 0 || plan is null)
        {
            Back(socket, held.Packets);

            return;
        }

        Interlocked.Increment(ref _named);
        _names.Learn(key.Source, key.Target, name);
        if (!_desk.Learn(plan, name, key.Target))
        {
            Back(socket, held.Packets);

            return;
        }

        _ = Task.Run(() => LandAsync(socket, plan, held.Packets));
    }

    // Takes one packet of a TCP connection: its bytes join what the client said so far, and once the name is read or
    // there is none the connection is let go or cut.
    private void Stream(NetfilterQueue socket, QueuedPacket packet, Flow key, int at)
    {
        if (StreamPiece.Read(packet.Payload, at) is not { } piece)
        {
            Back(socket, packet.Id);

            return;
        }

        if (!_said.TryGetValue(key, out var said))
        {
            if (_said.Count >= NameDefaults.MostHeld)
            {
                Back(socket, packet.Id);

                return;
            }

            said = new Said(_time.GetUtcNow());
            _said[key] = said;
        }

        if (said.IsEnding)
        {
            socket.Decide(packet.Id, NetfilterQueue.Drop);

            return;
        }

        var end = Math.Min(End(packet.Payload), packet.Payload.Length);
        if (end <= piece.Data)
        {
            said.Hello.Start(piece.Sequence);
            socket.Decide(packet.Id, NetfilterQueue.Accept);

            return;
        }

        var seen = said.Hello.Take(piece.Sequence, packet.Payload.AsSpan(piece.Data, end - piece.Data));
        said.Count++;
        if (seen.Reading == NameReading.More && said.Count < NameDefaults.MostPackets)
        {
            if (said.IsLoose)
            {
                socket.Decide(packet.Id, NetfilterQueue.Accept);
            }
            else
            {
                said.Packets.Add(packet.Id);
                said.Since = _time.GetUtcNow();
            }

            return;
        }

        var packets = said.Packets.Append(packet.Id).ToList();
        said.Packets.Clear();
        if (Settle(socket, key, packets, packet.Mark & RouteMarks, seen.Name))
        {
            said.IsEnding = true;
            said.Since = _time.GetUtcNow();
        }
        else
        {
            _said.Remove(key);
        }
    }

    // Decides a TCP connection whose name is known: it goes on, on the way the rules name or on another one, off
    // which they take the next connections by the address; one a blocking rule takes is cut, which the answer tells.
    private bool Settle(NetfilterQueue socket, Flow key, List<uint> packets, uint mark, string name)
    {
        _desk.Seen(key.Target);
        var plan = _plans.Held;
        if (name.Length == 0 || plan is null)
        {
            Back(socket, packets);

            return false;
        }

        Interlocked.Increment(ref _named);
        _names.Learn(key.Source, key.Target, name);
        var decision = _desk.Decide(plan, new RouteQuery
        {
            Name = name,
            Addresses = [key.Target],
            Port = key.Port,
            SourcePort = key.SourcePort,
            Protocol = RouteProtocol.Tcp,
            Sources = [key.Source],
            Inbound = _inbounds.Reach.Inbound(key.Source),
        });
        var told = _desk.Learn(plan, name, key.Target);
        if (decision.Goes || !Once(key))
        {
            if (decision.Misses(mark))
            {
                Interlocked.Increment(ref _missed);
            }

            Back(socket, packets);
            if (told)
            {
                _ = Task.Run(() => LandAsync(plan));
            }

            return false;
        }

        _ = Task.Run(() => CutAsync(socket, plan, key, packets));

        return true;
    }

    // Tells whether one more connection of a client to this address and port may be cut, counting it.
    private bool Once(Flow key)
    {
        var now = _time.GetUtcNow();
        var pair = new Pair(key.Source, key.Target, key.Port);
        if (_once.TryGetValue(pair, out var held) && held.Until > now)
        {
            if (held.Count >= NameDefaults.MostAgain)
            {
                return false;
            }

            _once[pair] = (held.Until, held.Count + 1);

            return true;
        }

        if (_once.Count >= MostOnce)
        {
            foreach (var old in _once.Where(one => one.Value.Until <= now).Select(one => one.Key).ToArray())
            {
                _once.Remove(old);
            }

            if (_once.Count >= MostOnce)
            {
                _once.Clear();
            }
        }

        _once[pair] = (now + NameDefaults.Once, 1);

        return true;
    }

    private async Task LandAsync(NetfilterQueue socket, RoutePlan plan, List<uint> packets)
    {
        _ = await LandAsync(plan).ConfigureAwait(false);
        try
        {
            Back(socket, packets);
        }
        catch (Exception ex) when (ex is NetlinkException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "the packets of a QUIC connection were not given back to the firewall");
        }
    }

    // Sends the addresses that wait into the sets of the rules on the host, telling whether the host took them.
    private async Task<bool> LandAsync(RoutePlan plan)
    {
        try
        {
            await _desk.LandAsync(plan, CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (HostNetworkException ex)
        {
            _logger.LogWarning(ex, "an address read out of a connection did not reach the sets of the rules");

            return false;
        }
    }

    // Notes an address as looked at on the host at once, telling whether the host took it.
    private async Task<bool> NotedAsync(IPAddress address)
    {
        try
        {
            await _desk.NoteAsync(address, CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is HostNetworkException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "an address looked at did not reach the host");

            return false;
        }
    }

    // Has the firewall reset a TCP connection a blocking rule takes once its address lies in the set of the rule and
    // among the addresses looked at, so that the rule drops the next ones by the address. A host that took neither
    // leaves the connection on its way.
    private async Task CutAsync(NetfilterQueue socket, RoutePlan plan, Flow key, List<uint> packets)
    {
        var landed = await LandAsync(plan).ConfigureAwait(false) && await NotedAsync(key.Target).ConfigureAwait(false);
        try
        {
            if (landed)
            {
                Interlocked.Increment(ref _reset);
            }

            for (var at = 0; at < packets.Count; at++)
            {
                if (!landed)
                {
                    Back(socket, packets[at]);
                }
                else if (at == 0)
                {
                    socket.Decide(packets[at], NetfilterQueue.Repeat, NameDefaults.Reset);
                }
                else
                {
                    socket.Decide(packets[at], NetfilterQueue.Drop);
                }
            }
        }
        catch (Exception ex) when (ex is NetlinkException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "the packets of a TCP connection were not given back to the firewall");
        }

        _ended.Enqueue(key);
    }

    // Lets the packets held too long go on without a name and forgets the connections that said nothing.
    private void Expire(NetfilterQueue socket)
    {
        while (_ended.TryDequeue(out var ended))
        {
            _said.Remove(ended);
        }

        var now = _time.GetUtcNow();
        if (now - _looked < NameDefaults.Wait || (_held.Count == 0 && _said.Count == 0))
        {
            return;
        }

        _looked = now;
        var due = now - NameDefaults.Hold;
        foreach (var key in _held.Where(pair => pair.Value.Since <= due).Select(pair => pair.Key).ToArray())
        {
            Back(socket, _held[key].Packets);
            _held.Remove(key);
        }

        var gone = now - TimeSpan.FromSeconds(NameDefaults.WaitSeconds);
        foreach (var (key, said) in _said.Where(pair => pair.Value.Since <= due).ToArray())
        {
            if (said.Packets.Count > 0)
            {
                foreach (var id in said.Packets)
                {
                    socket.Decide(id, NetfilterQueue.Accept);
                }

                said.Packets.Clear();
                said.IsLoose = true;
            }

            if (said.Since <= gone)
            {
                _said.Remove(key);
            }
        }
    }

    // Returns where an IP packet ends by its own header.
    private static int End(ReadOnlySpan<byte> packet) => (packet[0] >> 4) switch
    {
        4 when packet.Length >= 4 => BinaryPrimitives.ReadUInt16BigEndian(packet[2..]),
        6 when packet.Length >= 6 => 40 + BinaryPrimitives.ReadUInt16BigEndian(packet[4..]),
        _ => packet.Length,
    };

    private static void Back(NetfilterQueue socket, List<uint> packets)
    {
        foreach (var id in packets)
        {
            Back(socket, id);
        }
    }

    // Sends a packet through the chain once more, marked as read.
    private static void Back(NetfilterQueue socket, uint id) => socket.Decide(id, NetfilterQueue.Repeat, NameDefaults.Read);

    /// <summary>
    /// A connection as its packets name it.
    /// </summary>
    private sealed record Flow(IPAddress Source, int SourcePort, IPAddress Target, int Port);

    /// <summary>
    /// A client and where it went, whatever port it went from.
    /// </summary>
    private sealed record Pair(IPAddress Source, IPAddress Target, int Port);

    /// <summary>
    /// The packets of a QUIC connection the queue holds and what they told so far.
    /// </summary>
    private sealed class Held(DateTimeOffset since)
    {
        public DateTimeOffset Since { get; } = since;

        public QuicHello Hello { get; } = new();

        public List<uint> Packets { get; } = [];
    }

    /// <summary>
    /// What a TCP connection said so far and the packets of it the queue holds.
    /// </summary>
    private sealed class Said(DateTimeOffset since)
    {
        public DateTimeOffset Since { get; set; } = since;

        public StreamHello Hello { get; } = new();

        public List<uint> Packets { get; } = [];

        public int Count { get; set; }

        public bool IsLoose { get; set; }

        public bool IsEnding { get; set; }
    }
}
