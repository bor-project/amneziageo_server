using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Relay;

/// <summary>
/// Takes datagrams on a port and passes them to one target, holding a socket per client.
/// </summary>
public sealed class UdpRelay : IDisposable
{
    private const int Datagram = 65535;

    private readonly ConcurrentDictionary<IPEndPoint, RelaySession> _sessions = new();

    private readonly Socket _socket;

    private readonly IPEndPoint _target;

    private readonly TimeSpan _idle;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public UdpRelay(IPEndPoint listen, IPEndPoint target, TimeSpan idle, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(listen);
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        _idle = idle;
        _time = time ?? TimeProvider.System;
        _socket = new Socket(listen.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (listen.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _socket.DualMode = true;
        }

        _socket.Bind(listen);
    }

    /// <summary>
    /// The port the relay took.
    /// </summary>
    public int Port => _socket.LocalEndPoint is IPEndPoint bound ? bound.Port : 0;

    /// <summary>
    /// How many clients the relay holds.
    /// </summary>
    public int Clients => _sessions.Count;

    /// <summary>
    /// Carries datagrams both ways until the token is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var sweeping = SweepAsync(ct);
        var buffer = new byte[Datagram];
        var any = new IPEndPoint(
            _socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
            0);

        while (!ct.IsCancellationRequested)
        {
            var read = await TakeAsync(buffer, any, ct).ConfigureAwait(false);
            if (read is null)
            {
                break;
            }

            if (read.Value.RemoteEndPoint is not IPEndPoint { Port: > 0 } client)
            {
                continue;
            }

            var session = _sessions.GetOrAdd(client, Open);
            session.Seen = _time.GetUtcNow();
            await session.SendAsync(buffer.AsMemory(0, read.Value.ReceivedBytes), ct).ConfigureAwait(false);
        }

        await sweeping.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
        _socket.Dispose();
    }

    private async Task<SocketReceiveFromResult?> TakeAsync(byte[] buffer, IPEndPoint any, CancellationToken ct)
    {
        try
        {
            return await _socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, any, ct)
                .ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return new SocketReceiveFromResult { ReceivedBytes = 0, RemoteEndPoint = any };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private RelaySession Open(IPEndPoint client)
    {
        var session = new RelaySession(client, _target, _time.GetUtcNow());
        session.Answer(_socket, _time);

        return session;
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_idle, _time);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var edge = _time.GetUtcNow() - _idle;
                foreach (var pair in _sessions)
                {
                    if (pair.Value.Seen <= edge && _sessions.TryRemove(pair))
                    {
                        pair.Value.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
