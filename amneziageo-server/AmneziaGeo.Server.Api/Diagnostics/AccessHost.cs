using System.Net;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.Data.Sqlite;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// Takes the new connections the firewall logs and keeps them in the connection log.
/// </summary>
public sealed class AccessHost : BackgroundService
{
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Miss = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Complaining = TimeSpan.FromMinutes(1);

    private const int ReadBuffer = 1 << 18;

    private const int Permission = 1;

    private readonly SemaphoreSlim _turn = new(1, 1);

    private readonly AccessQueue _queue = new();

    private readonly AccessWatch _watch = new();

    private readonly IServiceScopeFactory _scopes;

    private readonly AccessGate _gate;

    private readonly AccessNames _names;

    private readonly AccessRecords _records;

    private readonly RoutePlans _plans;

    private readonly TimeProvider _time;

    private readonly ILogger<AccessHost> _logger;

    private readonly Dictionary<uint, string> _inbounds = [];

    private AccessSettings _settings = AccessSettings.Default;

    private NetfilterLog? _socket;

    private Thread? _reader;

    private volatile bool _reading;

    private Dictionary<IPAddress, string> _clients = [];

    private DateTimeOffset _clientsRead = DateTimeOffset.MinValue;

    private bool _missed;

    private DateTimeOffset _trimmed = DateTimeOffset.MinValue;

    private DateTimeOffset _complained = DateTimeOffset.MinValue;

    private long _written;

    private long _failed;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessHost(
        IServiceScopeFactory scopes,
        AccessGate gate,
        AccessNames names,
        AccessRecords records,
        RoutePlans plans,
        TimeProvider time,
        ILogger<AccessHost> logger)
    {
        _scopes = scopes;
        _gate = gate;
        _names = names;
        _records = records;
        _plans = plans;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The settings the log runs with.
    /// </summary>
    public AccessSettings Settings => Volatile.Read(ref _settings);

    /// <summary>
    /// Whether the new connections go to the log right now.
    /// </summary>
    public bool IsRunning => _gate.Group is not null;

    /// <summary>
    /// Why the log does not run although it is turned on, or null.
    /// </summary>
    public string? Fault => _gate.Fault;

    /// <summary>
    /// How many records were lost on the way to the log.
    /// </summary>
    public long Lost => _queue.Dropped + (_socket?.Overruns ?? 0) + Interlocked.Read(ref _failed);

    /// <summary>
    /// How many records were written since the panel started.
    /// </summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>
    /// Takes new settings and starts or stops the log to match them.
    /// </summary>
    public async Task TakeAsync(AccessSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _settings, settings);
            if (settings.IsEnabled)
            {
                Open();
                await SettleAsync(ct).ConfigureAwait(false);
            }
            else
            {
                Shut();
                await SettleAsync(ct).ConfigureAwait(false);
                StopReading();
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Removes every record of the log.
    /// </summary>
    public async Task ClearAsync(CancellationToken ct)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(_records.Clear, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await Task.Yield();
        try
        {
            if (!Prepare())
            {
                return;
            }

            using (var scope = _scopes.CreateScope())
            {
                var settings = await scope.ServiceProvider.GetRequiredService<AccessStore>().ReadAsync(stopping)
                    .ConfigureAwait(false);
                if (settings.IsEnabled)
                {
                    await TakeAsync(settings, stopping).ConfigureAwait(false);
                }
                else
                {
                    Volatile.Write(ref _settings, settings);
                }
            }

            using var timer = new PeriodicTimer(AccessDefaults.Gather, _time);
            while (await timer.WaitForNextTickAsync(stopping).ConfigureAwait(false))
            {
                await DrainAsync(stopping).ConfigureAwait(false);
                Trim();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Shut();
            StopReading();
            Drain();
            Write(_watch.Flush());
        }
    }

    private bool Prepare()
    {
        try
        {
            _records.Prepare();
            DatabaseFiles.Hide(_records.Location, _logger);

            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _gate.Close("the file of the log could not be opened");
            _logger.LogError(ex, "the connection log could not open {File}", _records.Location);

            return false;
        }
    }

    private void Open()
    {
        if (_socket is not null)
        {
            _gate.Open();
            _names.IsOn = true;
            return;
        }

        try
        {
            _socket = new NetfilterLog(AccessDefaults.Group, AccessDefaults.Snap, AccessDefaults.Buffer, AccessDefaults.Wait);
        }
        catch (Exception ex) when (ex is NetlinkException or DllNotFoundException or EntryPointNotFoundException)
        {
            _gate.Close(Why(ex));
            _logger.LogWarning(ex, "the connection log could not take the log group of the firewall");
            return;
        }

        _reading = true;
        _reader = new Thread(Read) { IsBackground = true, Name = "connection log" };
        _reader.Start(_socket);
        _gate.Open();
        _names.IsOn = true;
        _logger.LogInformation("the connection log takes the new connections of the clients");
    }

    private void Shut()
    {
        _gate.Close();
        _names.IsOn = false;
    }

    private void StopReading()
    {
        _reading = false;
        _reader?.Join(AccessDefaults.Wait * 4);
        _reader = null;
        _socket?.Dispose();
        _socket = null;
    }

    private void Read(object? state)
    {
        if (state is not NetfilterLog socket)
        {
            return;
        }

        var buffer = new byte[ReadBuffer];
        var packets = new List<NetfilterPacket>();
        while (_reading)
        {
            var read = Next(socket, buffer);
            if (read < 0)
            {
                return;
            }

            if (read == 0)
            {
                continue;
            }

            var at = _time.GetUtcNow();
            NetfilterLog.Parse(buffer.AsSpan(0, read), packets);
            foreach (var packet in packets)
            {
                _queue.Offer(new AccessCatch(at, packet));
            }

            packets.Clear();
        }
    }

    private int Next(NetfilterLog socket, byte[] buffer)
    {
        try
        {
            return socket.Read(buffer);
        }
        catch (NetlinkException ex)
        {
            _gate.Close(Why(ex));
            _logger.LogWarning(ex, "the connection log stopped reading the firewall");

            return -1;
        }
        catch (ObjectDisposedException)
        {
            return -1;
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        if (_queue.Reader.TryPeek(out _))
        {
            await ClientsAsync(ct).ConfigureAwait(false);
            Drain();
        }

        Write(IsRunning ? _watch.Due(_time.GetUtcNow()) : _watch.Flush());
    }

    private void Drain()
    {
        var plan = _plans.Held;
        while (_queue.Reader.TryRead(out var caught))
        {
            if (caught.Packet.Prefix == AccessTag.Reply)
            {
                if (AccessAnswer.Read(caught.Packet.Payload) is { } answer)
                {
                    _watch.Hear(answer, caught.At);
                }

                continue;
            }

            var record = AccessReader.Record(caught.Packet, caught.At, plan, Client, Inbound, _names);
            if (record is not null && !AccessReader.IsQuestion(record, plan))
            {
                _watch.Take(record);
            }
        }
    }

    private void Write(IReadOnlyList<AccessRecord> records)
    {
        foreach (var batch in records.Chunk(AccessDefaults.Batch))
        {
            try
            {
                _records.Write(batch);
                Interlocked.Add(ref _written, batch.Length);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                Interlocked.Add(ref _failed, batch.Length);
                Complain(() => _logger.LogWarning(ex, "the connection log could not write {Count} records", batch.Length));
            }
        }
    }

    private void Trim()
    {
        var now = _time.GetUtcNow();
        if (now - _trimmed < AccessDefaults.Trimming)
        {
            return;
        }

        _trimmed = now;
        try
        {
            var removed = _records.Trim(now.AddDays(-Settings.Days), AccessDefaults.MostRecords);
            if (removed > 0)
            {
                _logger.LogInformation("the connection log let {Count} old records go", removed);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Complain(() => _logger.LogWarning(ex, "the connection log could not let the records older than {Days} days go", Settings.Days));
        }
    }

    private async Task ClientsAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var age = now - _clientsRead;
        if (age < Refresh && !(_missed && age >= Miss))
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var clients = await scope.ServiceProvider.GetRequiredService<ClientStore>().ListAsync(ct).ConfigureAwait(false);
        var map = new Dictionary<IPAddress, string>();
        foreach (var client in clients)
        {
            foreach (var address in client.Address)
            {
                if (AwgAllowedIp.TryParse(address, out var range))
                {
                    map[range.Address] = client.Name;
                }
            }
        }

        _clients = map;
        _clientsRead = now;
        _missed = false;
        _inbounds.Clear();
    }

    private string Client(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (_clients.TryGetValue(plain, out var name))
        {
            return name;
        }

        _missed = true;

        return string.Empty;
    }

    private string Inbound(uint index)
    {
        if (!_inbounds.TryGetValue(index, out var name))
        {
            name = InterfaceIndex.Name(index);
            _inbounds[index] = name;
        }

        return name;
    }

    private async Task SettleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RouteApplier>().SettleAsync(ct).ConfigureAwait(false);
    }

    private void Complain(Action say)
    {
        var now = _time.GetUtcNow();
        if (now - _complained < Complaining)
        {
            return;
        }

        _complained = now;
        say();
    }

    private static string Why(Exception ex) => ex switch
    {
        NetlinkException { Error: NetfilterLog.Busy } => "another program holds the log group of the firewall",
        NetlinkException { Error: NetfilterLog.Unsupported } => "the kernel does not hand logged packets to programs",
        NetlinkException { Error: Permission } => "the panel may not read the log of the firewall",
        NetlinkException netlink => netlink.Message,
        _ => "the host cannot log connections",
    };
}
