using System.Buffers;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Names;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Names;

/// <summary>
/// Takes the TCP connections the firewall hands over, reads the name each of them goes to and carries it on the way
/// the rules say.
/// </summary>
public sealed class NameRelay : BackgroundService
{
    private const int LevelIp = 0;

    private const int LevelSocket = 1;

    private const int LevelIpv6 = 41;

    private const int IpTransparent = 19;

    private const int Ipv6Transparent = 75;

    private const int SocketMark = 36;

    private const int OriginalTarget = 80;

    private const int Room = 16384;

    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(NameDefaults.WaitSeconds);

    private readonly IServiceScopeFactory _scopes;

    private readonly RoutePlans _plans;

    private readonly NameDesk _desk;

    private readonly NameGate _gate;

    private readonly NameInbounds _inbounds;

    private readonly FirewallHost _firewall;

    private readonly IHostNetwork _network;

    private readonly AccessHost _access;

    private readonly AccessNames _names;

    private readonly NameOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<NameRelay> _logger;

    private long _taken;

    private long _taken6;

    private long _open;

    /// <summary>
    /// ctor
    /// </summary>
    public NameRelay(
        IServiceScopeFactory scopes,
        RoutePlans plans,
        NameDesk desk,
        NameGate gate,
        NameInbounds inbounds,
        FirewallHost firewall,
        IHostNetwork network,
        AccessHost access,
        AccessNames names,
        NameOptions options,
        TimeProvider time,
        ILogger<NameRelay> logger)
    {
        _scopes = scopes;
        _plans = plans;
        _desk = desk;
        _gate = gate;
        _inbounds = inbounds;
        _firewall = firewall;
        _network = network;
        _access = access;
        _names = names;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// How many connections the panel took over.
    /// </summary>
    public long Taken => Interlocked.Read(ref _taken);

    /// <summary>
    /// How many connections the panel carries right now.
    /// </summary>
    public long Open => Interlocked.Read(ref _open);

    private long Taken6 => Interlocked.Read(ref _taken6);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        await Task.Yield();
        if (!_options.Take)
        {
            await LeaveAsync(stopping).ConfigureAwait(false);

            return;
        }

        var doors = await DoorsAsync(stopping).ConfigureAwait(false);
        var guarded = await _firewall.DropsForeignAsync(stopping).ConfigureAwait(false);
        if (guarded && doors.Count == 0)
        {
            _logger.LogInformation(
                "the firewall of the host drops what comes in for another address and lets in no port the panel holds: "
                + "the panel takes no TCP connections over and reads their names in the queue");
            await LeaveAsync(stopping).ConfigureAwait(false);

            return;
        }

        var listeners = new List<Socket>();
        try
        {
            listeners.Add(Listener(AddressFamily.InterNetwork, _options.Port));
            await _network.RuleAsync(NameDefaults.Taken, NameDefaults.Table, NameDefaults.Priority, true, stopping)
                .ConfigureAwait(false);
            await _network.LocalRouteAsync(NameDefaults.Table, stopping).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or HostNetworkException)
        {
            Shut(listeners);
            _logger.LogWarning(ex, "the panel could not take the TCP connections of the clients over");

            return;
        }

        var six = Six(listeners);
        var door = guarded ? doors[0] : (int?)null;
        await _inbounds.RenewAsync(stopping).ConfigureAwait(false);
        _gate.OpenPort(_options.Port, six, door);
        _logger.LogInformation(
            "the panel takes the first TCP connections of the clients over on port {Port}, IPv6 {Six}",
            _options.Port,
            six ? "too" : "left to the rules");
        if (door is { } through)
        {
            _logger.LogInformation(
                "the firewall of the host drops what comes in for another address: the connections handed over come in "
                + "through port {Door}, which it lets in",
                through);
        }

        await SettleAsync(stopping).ConfigureAwait(false);
        try
        {
            var tasks = listeners.Select(listener => AcceptAsync(listener, stopping)).ToList();
            tasks.Add(TendAsync(six, door, doors, stopping));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_gate.Offer?.Port is not null)
            {
                _gate.ClosePort();
                await SettleAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Shut(listeners);
        }
    }

    // Takes the rule that delivers the connections handed over to the panel out of the host.
    private async Task LeaveAsync(CancellationToken ct)
    {
        try
        {
            await _network.RuleAsync(NameDefaults.Taken, NameDefaults.Table, NameDefaults.Priority, false, ct)
                .ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            _logger.LogDebug(ex, "the rule of the connections handed over stayed on the host");
        }
    }

    // Returns the TCP ports of the host the connections handed over may come in through, the likeliest first: the
    // ones the panel holds and the firewall of the host lets in.
    private async Task<IReadOnlyList<int>> DoorsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var plan = await scope.ServiceProvider.GetRequiredService<FirewallApplier>().PlanAsync(ct).ConfigureAwait(false);
            var panel = await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(ct).ConfigureAwait(false);
            var ports = NameDoor.Ports(plan, panel);
            var states = await _firewall
                .StatesAsync([.. ports.Select(port => new FirewallPort(FirewallPlan.Tcp, port, string.Empty))], ct)
                .ConfigureAwait(false);

            return NameDoor.Pick(ports, states);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "the ports the firewall of the host lets in were not read");

            return [];
        }
    }

    // Adds the listener of IPv6 where the host carries the family, telling whether it did.
    private bool Six(List<Socket> listeners)
    {
        if (!Socket.OSSupportsIPv6)
        {
            return false;
        }

        try
        {
            listeners.Add(Listener(AddressFamily.InterNetworkV6, _options.Port));

            return true;
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "the panel takes no IPv6 connections over");

            return false;
        }
    }

    // Returns a listening socket on the loopback that takes what the firewall hands over, whatever address it goes to.
    private static Socket Listener(AddressFamily family, int port)
    {
        var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            if (family == AddressFamily.InterNetworkV6)
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                socket.SetRawSocketOption(LevelIpv6, Ipv6Transparent, BitConverter.GetBytes(1));
                socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, port));
            }
            else
            {
                socket.SetRawSocketOption(LevelIp, IpTransparent, BitConverter.GetBytes(1));
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }

            socket.Listen(1024);

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static void Shut(List<Socket> listeners)
    {
        foreach (var listener in listeners)
        {
            listener.Dispose();
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
            _logger.LogWarning(ex, "the rules were not laid anew for the connections the panel takes over");
        }
    }

    // Takes the connections of one listener; one that comes while the panel carries a second's worth more than it
    // takes is cut.
    private async Task AcceptAsync(Socket listener, CancellationToken ct)
    {
        var six = listener.AddressFamily == AddressFamily.InterNetworkV6;
        while (!ct.IsCancellationRequested)
        {
            var client = await NextAsync(listener, ct).ConfigureAwait(false);
            if (client is null)
            {
                continue;
            }

            Interlocked.Increment(ref _taken);
            if (six)
            {
                Interlocked.Increment(ref _taken6);
            }

            if (Open >= _options.Most + NameDefaults.MostTaken)
            {
                client.Close(0);
                continue;
            }

            _ = CarryAsync(client, ct);
        }
    }

    // Returns the next connection, or null after a failure of the listener that is waited out.
    private async Task<Socket?> NextAsync(Socket listener, CancellationToken ct)
    {
        try
        {
            return await listener.AcceptAsync(ct).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "a connection handed over was not taken");
            await Task.Delay(NameDefaults.Noting, ct).ConfigureAwait(false);

            return null;
        }
    }

    // Sends the addresses looked at to the host, has the firewall hand nothing over while the panel is full, and
    // watches that what is handed over reaches the panel: where it does not, the connections are sent in through a
    // port of the host, and where that does not help either the panel takes none.
    private async Task TendAsync(bool six, int? door, IReadOnlyList<int> doors, CancellationToken ct)
    {
        var checkedAt = _time.GetUtcNow();
        var four = new NameWatch();
        var sixth = new NameWatch();
        four.Start(Taken - Taken6);
        sixth.Start(Taken6);
        var full = false;
        var shut = false;
        using var timer = new PeriodicTimer(NameDefaults.Noting, _time);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await _desk.NoteAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HostNetworkException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "the addresses looked at did not reach the host");
            }

            if (shut)
            {
                continue;
            }

            var now = _time.GetUtcNow();
            if (NameRoom.Full(full, Open, _options.Most) != full)
            {
                full = !full;
                if (full)
                {
                    _gate.ClosePort();
                    _logger.LogWarning(
                        "the panel carries {Count} connections, the most it takes: the firewall hands it no more until "
                        + "a quarter of them ended",
                        Open);
                }
                else
                {
                    _gate.OpenPort(_options.Port, six, door);
                    _logger.LogInformation("the panel has room again and takes the first TCP connections of the clients over");
                }

                await SettleAsync(ct).ConfigureAwait(false);
                four.Start(Taken - Taken6);
                sixth.Start(Taken6);
                checkedAt = now;

                continue;
            }

            if (full || now - checkedAt < NameDefaults.Checking)
            {
                continue;
            }

            checkedAt = now;
            var lost = four.Look(await HandedAsync(RouteRuleset.TakenCounter, ct).ConfigureAwait(false), Taken - Taken6);
            var lost6 = six && sixth.Look(await HandedAsync(RouteRuleset.TakenCounter6, ct).ConfigureAwait(false), Taken6);
            if (lost && door is null && doors.Count > 0)
            {
                door = doors[0];
                _gate.OpenPort(_options.Port, six, door);
                _logger.LogWarning(
                    "{Count} packets were handed over and no connection reached the panel, the host drops them on the way "
                    + "in: the panel has them sent in through port {Door}",
                    four.Unreached,
                    door);
            }
            else if (lost)
            {
                shut = true;
                _gate.ClosePort();
                _logger.LogWarning(
                    "{Count} packets were handed over and no connection reached the panel, the host drops them on the way in: "
                    + "the panel takes no more TCP connections over and reads their names in the queue",
                    four.Unreached);
            }
            else if (lost6)
            {
                six = false;
                _gate.OpenPort(_options.Port, false, door);
                _logger.LogWarning(
                    "{Count} packets of IPv6 were handed over and no connection reached the panel: the panel takes the TCP "
                    + "connections of IPv6 over no more and reads their names in the queue",
                    sixth.Unreached);
            }
            else
            {
                continue;
            }

            await SettleAsync(ct).ConfigureAwait(false);
            four.Start(Taken - Taken6);
            sixth.Start(Taken6);
        }
    }

    // Returns how many packets the firewall handed over by a counter, or null when the host tells none.
    private async Task<long?> HandedAsync(string counter, CancellationToken ct)
    {
        try
        {
            return await _network.CounterAsync(RouteRuleset.TableName, counter, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            _logger.LogDebug(ex, "the counter of what is handed over was not read");

            return null;
        }
    }

    // Carries one connection: what the client says first is read, the rules decide, the far end is dialled and both
    // ways are copied.
    private async Task CarryAsync(Socket client, CancellationToken ct)
    {
        Interlocked.Increment(ref _open);
        var first = ArrayPool<byte>.Shared.Rent(NameDefaults.MostSaid);
        var far = default(Socket);
        var told = new Told(this);
        try
        {
            Tune(client);
            var source = (IPEndPoint)client.RemoteEndPoint!;
            var target = Target(client);
            var reach = _inbounds.Reach;
            if (!NameDefaults.Ports.Contains(target.Port) || !reach.Lets(source.Address, target.Address))
            {
                client.Close(0);

                return;
            }

            var trip = new Trip(this, source, target, reach.Inbound(source.Address), told);
            var said = await SaidAsync(client, first, ct).ConfigureAwait(false);
            var guess = said.IsSilent ? trip.Decide(string.Empty) : null;
            var dial = default(Task<string>);
            var spoke = NameSpoke.Client;
            if (guess is not null)
            {
                ArrayPool<byte>.Shared.Return(first);
                first = null;
                if (guess.Goes)
                {
                    far = Far(target, guess.Mark);
                    dial = ReachAsync(far, target, ct);
                }

                spoke = await NameFirst.SpokeAsync(client, far, dial, Whole, ct).ConfigureAwait(false);
                if (spoke == NameSpoke.Client)
                {
                    first = ArrayPool<byte>.Shared.Rent(NameDefaults.MostSaid);
                    said = await SaidAsync(client, first, ct).ConfigureAwait(false);
                }
            }

            var decision = guess is not null && spoke != NameSpoke.Client
                ? guess
                : said.Held > 0 ? trip.Decide(said.Name) : null;
            if (decision is null)
            {
                return;
            }

            await trip.SettleAsync(said.Name, decision, spoke != NameSpoke.Nobody, ct).ConfigureAwait(false);
            if (!decision.Goes)
            {
                told.End(string.Empty);
                client.Close(0);

                return;
            }

            if (far is null || dial is null || guess is null || !decision.Keeps(guess.Mark))
            {
                far?.Dispose();
                far = Far(target, decision.Mark);
                dial = ReachAsync(far, target, ct);
            }

            var outcome = await dial.ConfigureAwait(false);
            if (outcome.Length > 0)
            {
                told.End(outcome);

                return;
            }

            told.Later(AccessDefaults.Answer);
            if (first is not null)
            {
                await SendAllAsync(far, first.AsMemory(0, said.Held), ct).ConfigureAwait(false);
                ArrayPool<byte>.Shared.Return(first);
                first = null;
            }

            using var both = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAll(CopyAsync(client, far, null, both), CopyAsync(far, client, told, both)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            told.End(AccessOutcome.Reset);
        }
        finally
        {
            if (first is not null)
            {
                ArrayPool<byte>.Shared.Return(first);
            }

            client.Dispose();
            far?.Dispose();
            Interlocked.Decrement(ref _open);
        }
    }

    private static void Tune(Socket socket)
    {
        socket.NoDelay = true;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 120);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 30);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 4);
    }

    // Returns where the client was going: the address the kernel kept when the firewall sent the connection in
    // through a port of the host, or the one the connection came in with.
    private static IPEndPoint Target(Socket client)
    {
        var came = (IPEndPoint)client.LocalEndPoint!;
        var six = client.AddressFamily == AddressFamily.InterNetworkV6;
        Span<byte> name = stackalloc byte[28];
        try
        {
            var read = client.GetRawSocketOption(six ? LevelIpv6 : LevelIp, OriginalTarget, name);

            return NameTarget.Read(name[..read]) ?? came;
        }
        catch (SocketException)
        {
            return came;
        }
    }

    // Reads what the client says first, waiting a short while for its first byte and longer for the rest.
    private static Task<NameWord> SaidAsync(Socket client, byte[] first, CancellationToken ct) =>
        NameFirst.SaidAsync(client, first, NameDefaults.Said, Whole, ct);

    // Makes the socket the far end is reached from, on the way a mark names.
    private static Socket Far(IPEndPoint target, uint mark)
    {
        var far = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            Tune(far);
            if (mark != 0)
            {
                far.SetRawSocketOption(LevelSocket, SocketMark, BitConverter.GetBytes(mark));
            }

            return far;
        }
        catch
        {
            far.Dispose();
            throw;
        }
    }

    // Lays the address into the sets of the rules its name falls under and notes it as looked at.
    private async Task LearnAsync(RoutePlan? plan, string name, IPAddress client, IPAddress target, CancellationToken ct)
    {
        _desk.Seen(target);
        if (plan is null || name.Length == 0)
        {
            return;
        }

        _names.Learn(client, target, name);
        if (!_desk.Learn(plan, name, target))
        {
            return;
        }

        try
        {
            await _desk.LandAsync(plan, ct).ConfigureAwait(false);
            await _desk.NoteAsync(ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            _logger.LogWarning(ex, "an address read out of a TCP connection did not reach the sets of the rules");
        }
    }

    // Reaches the far end and returns why it was not reached, nothing when it was.
    private static async Task<string> ReachAsync(Socket far, IPEndPoint target, CancellationToken ct)
    {
        using var dial = CancellationTokenSource.CreateLinkedTokenSource(ct);
        dial.CancelAfter(NameDefaults.Dial);
        try
        {
            await far.ConnectAsync(target, dial.Token).ConfigureAwait(false);

            return string.Empty;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AccessOutcome.Silent;
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.ConnectionRefused ? AccessOutcome.Reset : AccessOutcome.Unreachable;
        }
        catch (ObjectDisposedException)
        {
            return AccessOutcome.Reset;
        }
    }

    // Copies one way until the source ends, holding a buffer only while bytes come; what the far end sends first
    // settles the record of the connection.
    private static async Task CopyAsync(Socket from, Socket to, Told? told, CancellationTokenSource both)
    {
        var buffer = default(byte[]);
        try
        {
            while (true)
            {
                if (buffer is null)
                {
                    await from.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, both.Token).ConfigureAwait(false);
                    buffer = ArrayPool<byte>.Shared.Rent(Room);
                }

                var read = await from.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, both.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    told?.End(AccessOutcome.Reset);
                    break;
                }

                told?.End(AccessOutcome.Ok);
                await SendAllAsync(to, buffer.AsMemory(0, read), both.Token).ConfigureAwait(false);
                if (read < buffer.Length)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = null;
                }
            }

            to.Shutdown(SocketShutdown.Send);
        }
        catch
        {
            both.Cancel();
            throw;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static async ValueTask SendAllAsync(Socket to, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        while (data.Length > 0)
        {
            var sent = await to.SendAsync(data, SocketFlags.None, ct).ConfigureAwait(false);
            data = data[sent..];
        }
    }

    /// <summary>
    /// Where one connection the panel took comes from and goes, and what the rules say about it.
    /// </summary>
    private sealed class Trip(NameRelay relay, IPEndPoint source, IPEndPoint target, string inbound, Told told)
    {
        private RoutePlan? _plan;

        public NameDecision Decide(string name)
        {
            _plan = relay._plans.Held;

            return _plan is null
                ? new NameDecision(RouteProbe.Host, null, 0)
                : relay._desk.Decide(_plan, new RouteQuery
                {
                    Name = name,
                    Addresses = [target.Address],
                    Port = target.Port,
                    SourcePort = source.Port,
                    Protocol = RouteProtocol.Tcp,
                    Sources = [source.Address],
                    Inbound = inbound,
                });
        }

        public Task SettleAsync(string name, NameDecision decision, bool looked, CancellationToken ct)
        {
            told.Start(relay._time.GetUtcNow(), source, target, inbound, name, decision, _plan);

            return looked ? relay.LearnAsync(_plan, name, source.Address, target.Address, ct) : Task.CompletedTask;
        }
    }

    /// <summary>
    /// The record of one connection the panel carries, written to the log once its outcome is known.
    /// </summary>
    private sealed class Told(NameRelay relay)
    {
        private AccessRecord? _record;

        private int _ended;

        public void Start(
            DateTimeOffset at,
            IPEndPoint source,
            IPEndPoint target,
            string inbound,
            string name,
            NameDecision decision,
            RoutePlan? plan)
        {
            _record = AccessReader.Carried(at, source, target, inbound, name, decision.Verdict, decision.Leg, plan, decision.Mark);
        }

        public void End(string outcome)
        {
            if (_record is { } record && Interlocked.Exchange(ref _ended, 1) == 0)
            {
                relay._access.Carried(record with { Outcome = outcome });
            }
        }

        public void Later(TimeSpan wait) =>
            _ = Task.Delay(wait, relay._time).ContinueWith(_ => End(AccessOutcome.Empty), TaskScheduler.Default);
    }
}
