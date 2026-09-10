using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Relay;

/// <summary>
/// One client of a relay, with the socket its datagrams leave through.
/// </summary>
public sealed class RelaySession : IDisposable
{
    private const int Datagram = 65535;

    private readonly CancellationTokenSource _stopping = new();

    private readonly IPEndPoint _client;

    private readonly Socket _socket;

    /// <summary>
    /// ctor
    /// </summary>
    public RelaySession(IPEndPoint client, IPEndPoint target, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(target);

        _client = client;
        _socket = new Socket(target.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        _socket.Connect(target);
        Seen = at;
    }

    /// <summary>
    /// When the last datagram of the client went through.
    /// </summary>
    public DateTimeOffset Seen { get; set; }

    /// <summary>
    /// Passes a datagram of the client to the target.
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken ct)
    {
        try
        {
            await _socket.SendAsync(datagram, SocketFlags.None, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Carries what the target answers back to the client.
    /// </summary>
    public void Answer(Socket back, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(back);
        ArgumentNullException.ThrowIfNull(time);

        _ = Task.Run(() => CarryAsync(back, time), _stopping.Token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stopping.Cancel();
        _socket.Dispose();
        _stopping.Dispose();
    }

    private async Task CarryAsync(Socket back, TimeProvider time)
    {
        var buffer = new byte[Datagram];
        while (!_stopping.IsCancellationRequested)
        {
            var read = await ReadAsync(buffer).ConfigureAwait(false);
            if (read < 0)
            {
                break;
            }

            if (read == 0)
            {
                continue;
            }

            Seen = time.GetUtcNow();
            await WriteAsync(back, buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }

    private async Task<int> ReadAsync(byte[] buffer)
    {
        try
        {
            return await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, _stopping.Token)
                .ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return -1;
        }
        catch (OperationCanceledException)
        {
            return -1;
        }
    }

    private async Task WriteAsync(Socket back, ReadOnlyMemory<byte> datagram)
    {
        try
        {
            await back.SendToAsync(datagram, SocketFlags.None, _client, _stopping.Token).ConfigureAwait(false);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}
