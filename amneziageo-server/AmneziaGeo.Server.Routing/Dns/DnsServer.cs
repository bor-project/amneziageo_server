using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Takes the questions of the clients on the addresses of the tunnels.
/// </summary>
public sealed class DnsServer : IAsyncDisposable
{
    private const int PacketLimit = 4096;

    private const long HostAddressesMs = 30_000;

    private readonly DnsResolver _resolver;

    private readonly Func<IPEndPoint, IPEndPoint, bool, IPAddress?>? _origin;

    private readonly Func<IReadOnlyList<HostRoute>> _routes;

    private readonly List<Socket> _sockets = [];

    private readonly Lock _sync = new();

    private CancellationTokenSource? _life;

    private IPAddress[] _host = [];

    private long? _hostRead;

    private IReadOnlyList<HostRoute> _held = [];

    private long? _heldRead;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsServer(
        DnsResolver resolver,
        Func<IPEndPoint, IPEndPoint, bool, IPAddress?>? origin = null,
        Func<IReadOnlyList<HostRoute>>? routes = null)
    {
        _resolver = resolver;
        _origin = origin;
        _routes = routes ?? HostRoutes.Read;
    }

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
            var asked = Asked(client, socket.LocalEndPoint, false);
            var answer = await _resolver.AnswerAsync(question, false, (client as IPEndPoint)?.Address, asked, ct)
                .ConfigureAwait(false);
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
        var from = (held.RemoteEndPoint as IPEndPoint)?.Address;
        var head = new byte[2];
        try
        {
            var asked = Asked(held.RemoteEndPoint, held.LocalEndPoint, true);
            while (await FillAsync(held, head, ct).ConfigureAwait(false))
            {
                var question = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
                if (question.Length == 0 || !await FillAsync(held, question, ct).ConfigureAwait(false))
                {
                    return;
                }

                var answer = await _resolver.AnswerAsync(question, true, from, asked, ct).ConfigureAwait(false);
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

    // Returns the private name server a client asked before its question was turned to the resolver, or null when it
    // asked the resolver, a public server, the host itself or a network the host reaches by its default route.
    private IPAddress? Asked(EndPoint? client, EndPoint? local, bool stream)
    {
        if (_origin is null || client is not IPEndPoint from || local is not IPEndPoint to)
        {
            return null;
        }

        var asked = _origin(from, to, stream);

        return asked is not null
            && !asked.Equals(to.Address)
            && RouteProbe.Within(DnsDefaults.Private, asked)
            && !HostAddresses().Contains(asked)
            && HostRoutes.Behind(asked, Routes())
            ? asked
            : null;
    }

    // Returns the routes of the host, read again once they are half a minute old.
    private IReadOnlyList<HostRoute> Routes()
    {
        var now = Environment.TickCount64;
        lock (_sync)
        {
            if (_heldRead is { } read && now - read < HostAddressesMs)
            {
                return _held;
            }

            _held = _routes();
            _heldRead = now;

            return _held;
        }
    }

    // Returns the addresses the host holds, read again once they are half a minute old.
    private IPAddress[] HostAddresses()
    {
        var now = Environment.TickCount64;
        lock (_sync)
        {
            if (_hostRead is { } read && now - read < HostAddressesMs)
            {
                return _host;
            }

            try
            {
                _host = [.. NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(face => face.GetIPProperties().UnicastAddresses)
                    .Select(unicast => unicast.Address)];
            }
            catch (NetworkInformationException)
            {
            }

            _hostRead = now;

            return _host;
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
