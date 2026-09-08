using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Takes the questions of the clients on the addresses of the tunnels.
/// </summary>
public sealed class DnsServer : IAsyncDisposable
{
    private const int PacketLimit = 4096;

    private readonly DnsResolver _resolver;

    private readonly List<Socket> _sockets = [];

    private CancellationTokenSource? _life;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsServer(DnsResolver resolver) => _resolver = resolver;

    /// <summary>
    /// Tells whether the resolver holds its sockets.
    /// </summary>
    public bool IsRunning => _life is not null;

    /// <summary>
    /// Takes the addresses that are on the host and starts answering on them.
    /// </summary>
    public IReadOnlyList<IPAddress> Start(IReadOnlyList<IPAddress> addresses, int port)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        Stop();
        var life = new CancellationTokenSource();
        var taken = new List<IPAddress>();
        var refused = default(SocketException);
        foreach (var address in addresses)
        {
            try
            {
                Packets(new IPEndPoint(address, port), life.Token);
                Streams(new IPEndPoint(address, port), life.Token);
                taken.Add(address);
            }
            catch (SocketException ex)
            {
                refused = ex;
            }
        }

        if (taken.Count == 0)
        {
            life.Cancel();
            life.Dispose();
            Close();

            throw refused ?? new SocketException((int)SocketError.AddressNotAvailable);
        }

        _life = life;

        return taken;
    }

    /// <summary>
    /// Drops the sockets and stops answering.
    /// </summary>
    public void Stop()
    {
        var life = _life;
        _life = null;
        if (life is not null)
        {
            life.Cancel();
            life.Dispose();
        }

        Close();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Stop();

        return ValueTask.CompletedTask;
    }

    private void Packets(IPEndPoint point, CancellationToken ct)
    {
        var socket = new Socket(point.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        Bind(socket, point);
        _ = Task.Run(() => PacketLoopAsync(socket, ct), CancellationToken.None);
    }

    private void Streams(IPEndPoint point, CancellationToken ct)
    {
        var socket = new Socket(point.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        Bind(socket, point);
        socket.Listen(64);
        _ = Task.Run(() => StreamLoopAsync(socket, ct), CancellationToken.None);
    }

    private void Bind(Socket socket, IPEndPoint point)
    {
        if (point.AddressFamily == AddressFamily.InterNetworkV6)
        {
            socket.DualMode = false;
        }

        socket.Bind(point);
        _sockets.Add(socket);
    }

    private async Task PacketLoopAsync(Socket socket, CancellationToken ct)
    {
        var buffer = new byte[PacketLimit];
        var from = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var taken = await socket.ReceiveFromAsync(buffer, SocketFlags.None, from, ct).ConfigureAwait(false);
                var question = buffer[..taken.ReceivedBytes];
                _ = Task.Run(() => ReplyAsync(socket, question, taken.RemoteEndPoint, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
            }
        }
    }

    private async Task ReplyAsync(Socket socket, byte[] question, EndPoint client, CancellationToken ct)
    {
        try
        {
            var answer = await _resolver.AnswerAsync(question, false, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                await socket.SendToAsync(answer, SocketFlags.None, client, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task StreamLoopAsync(Socket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await socket.AcceptAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
            }
        }
    }

    private async Task ServeAsync(Socket client, CancellationToken ct)
    {
        using var held = client;
        var head = new byte[2];
        try
        {
            while (await FillAsync(held, head, ct).ConfigureAwait(false))
            {
                var question = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
                if (question.Length == 0 || !await FillAsync(held, question, ct).ConfigureAwait(false))
                {
                    return;
                }

                var answer = await _resolver.AnswerAsync(question, true, ct).ConfigureAwait(false);
                if (answer is null)
                {
                    return;
                }

                BinaryPrimitives.WriteUInt16BigEndian(head, (ushort)answer.Length);
                await held.SendAsync(head, SocketFlags.None, ct).ConfigureAwait(false);
                await held.SendAsync(answer, SocketFlags.None, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static async Task<bool> FillAsync(Socket socket, Memory<byte> buffer, CancellationToken ct)
    {
        var at = 0;
        while (at < buffer.Length)
        {
            var read = await socket.ReceiveAsync(buffer[at..], SocketFlags.None, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            at += read;
        }

        return true;
    }

    private void Close()
    {
        foreach (var socket in _sockets)
        {
            try
            {
                socket.Dispose();
            }
            catch (SocketException)
            {
            }
        }

        _sockets.Clear();
    }
}
