using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Passes a question on to a name server outside.
/// </summary>
public interface IDnsUpstream
{
    /// <summary>
    /// Returns the answer of a name server, or null when none answered.
    /// </summary>
    Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct);
}

/// <summary>
/// Asks the name servers the settings name, each the way it is written, taking the first that answers.
/// </summary>
public sealed class DnsUpstream : IDnsUpstream, IDisposable
{
    /// <summary>
    /// The largest answer a name server may send.
    /// </summary>
    public const int MaxAnswer = 65535;

    /// <summary>
    /// How many questions in a row go unanswered before the fault is named.
    /// </summary>
    public const int Misses = 3;

    /// <summary>
    /// How long a name server that gave no answer is asked after the ones that answer.
    /// </summary>
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(30);

    private readonly Server[] _servers;

    private readonly TimeSpan _wait;

    private readonly Func<uint?> _way;

    private readonly TimeProvider _time;

    private readonly DnsTlsLinks _tls;

    private readonly DnsHttpsLinks _https;

    private int _missed;

    private string _reason = string.Empty;

    private string? _fault;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsUpstream(
        IReadOnlyList<string> servers,
        TimeSpan wait,
        Func<uint?>? way = null,
        X509Certificate2Collection? roots = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(servers);

        _servers = [.. servers.Select(one => DnsRules.Server(one, out var read) ? new Server(read) : null).OfType<Server>()];
        _wait = wait;
        _way = way ?? (() => 0u);
        _time = time ?? TimeProvider.System;
        _tls = new DnsTlsLinks(roots, _time);
        _https = new DnsHttpsLinks(roots);
    }

    /// <summary>
    /// Why the questions go unanswered, or null while a name server answers.
    /// </summary>
    public string? Fault => Volatile.Read(ref _fault);

    /// <inheritdoc/>
    public async Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
    {
        if (_way() is not { } mark)
        {
            return null;
        }

        var order = Order();
        Recall(order, question, stream, mark);
        foreach (var server in order)
        {
            var answer = await OneAsync(server, question, stream, mark, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                Interlocked.Exchange(ref _missed, 0);
                Volatile.Write(ref _fault, null);

                return answer;
            }
        }

        if (order.Count > 0 && Interlocked.Increment(ref _missed) >= Misses)
        {
            Volatile.Write(ref _fault, "no name server answers: " + Volatile.Read(ref _reason));
        }

        return null;
    }

    /// <summary>
    /// Closes the connections kept to the name servers.
    /// </summary>
    public void Dispose()
    {
        _tls.Dispose();
        _https.Dispose();
    }

    // Puts the name servers that answered last ahead of the ones that did not.
    private List<Server> Order() =>
        [.. _servers.Where(server => !server.Silent), .. _servers.Where(server => server.Silent)];

    // Asks a silent name server that has rested the same question aside, so no client waits for it to come back.
    private void Recall(List<Server> order, ReadOnlyMemory<byte> question, bool stream, uint mark)
    {
        if (order.Count == 0 || order[0].Silent)
        {
            return;
        }

        foreach (var server in order.Where(one => one.Silent && one.Rested(_time) && one.Claim()))
        {
            _ = AsideAsync(server, question.ToArray(), stream, mark);
        }
    }

    private async Task AsideAsync(Server server, byte[] question, bool stream, uint mark)
    {
        try
        {
            await OneAsync(server, question, stream, mark, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            server.Release();
        }
    }

    private async Task<byte[]?> OneAsync(
        Server server, ReadOnlyMemory<byte> question, bool stream, uint mark, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_wait);
        try
        {
            var answer = await SendAsync(server.Address, question, stream, mark, limit.Token).ConfigureAwait(false);
            Note(server, answer is null ? "it sent no answer" : null);

            return answer;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Note(server, string.Create(CultureInfo.InvariantCulture, $"it did not answer in {_wait.TotalSeconds:0.#} s"));

            return null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or HttpRequestException or ObjectDisposedException)
        {
            Note(server, Told(ex));

            return null;
        }
    }

    private Task<byte[]?> SendAsync(
        DnsUpstreamAddress server, ReadOnlyMemory<byte> question, bool stream, uint mark, CancellationToken ct) =>
        server.Transport switch
        {
            DnsTransport.Tls => _tls.AskAsync(server.Point, question, mark, ct),
            DnsTransport.Https => _https.AskAsync(server, question, mark, ct),
            _ => stream ? StreamAsync(server.Point, question, mark, ct) : PacketAsync(server.Point, question, mark, ct),
        };

    // Keeps whether a name server answered, and what it said when it did not.
    private void Note(Server server, string? reason)
    {
        if (reason is null)
        {
            server.Answered();

            return;
        }

        server.Missed(_time);
        Volatile.Write(ref _reason, $"{server.Address}: {reason}");
    }

    // Returns what stopped a question, in the words of the innermost failure.
    private static string Told(Exception ex)
    {
        var deepest = ex;
        while (deepest.InnerException is { } inner)
        {
            deepest = inner;
        }

        return deepest.Message.Split('\n')[0].Trim();
    }

    private static async Task<byte[]?> PacketAsync(
        IPEndPoint server, ReadOnlyMemory<byte> question, uint mark, CancellationToken ct)
    {
        using var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (!OutboundMark.Put(socket, mark))
        {
            return null;
        }

        await socket.ConnectAsync(server, ct).ConfigureAwait(false);
        await socket.SendAsync(question, SocketFlags.None, ct).ConfigureAwait(false);
        var buffer = new byte[4096];
        var read = await socket.ReceiveAsync(buffer, SocketFlags.None, ct).ConfigureAwait(false);

        return read < DnsMessage.HeaderLength || !Same(question.Span, buffer) ? null : buffer[..read];
    }

    private static async Task<byte[]?> StreamAsync(
        IPEndPoint server, ReadOnlyMemory<byte> question, uint mark, CancellationToken ct)
    {
        using var socket = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (!OutboundMark.Put(socket, mark))
        {
            return null;
        }

        await socket.ConnectAsync(server, ct).ConfigureAwait(false);
        var head = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(head, (ushort)question.Length);
        await socket.SendAsync(head, SocketFlags.None, ct).ConfigureAwait(false);
        await socket.SendAsync(question, SocketFlags.None, ct).ConfigureAwait(false);
        if (!await FillAsync(socket, head, ct).ConfigureAwait(false))
        {
            return null;
        }

        var answer = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];

        return await FillAsync(socket, answer, ct).ConfigureAwait(false) ? answer : null;
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

        return buffer.Length >= DnsMessage.HeaderLength;
    }

    private static bool Same(ReadOnlySpan<byte> question, ReadOnlySpan<byte> answer) =>
        question.Length >= 2 && answer[0] == question[0] && answer[1] == question[1];

    // A name server and whether it answered the last time it was asked.
    private sealed class Server(DnsUpstreamAddress address)
    {
        private long _since;

        private int _asked;

        private volatile bool _silent;

        public DnsUpstreamAddress Address { get; } = address;

        public bool Silent => _silent;

        // Tells whether the rest of a silent name server is over.
        public bool Rested(TimeProvider time) =>
            time.GetUtcNow().UtcTicks - Volatile.Read(ref _since) >= Rest.Ticks;

        public void Answered() => _silent = false;

        public void Missed(TimeProvider time)
        {
            Volatile.Write(ref _since, time.GetUtcNow().UtcTicks);
            _silent = true;
        }

        // Takes the turn to ask the name server aside, telling whether it was free.
        public bool Claim() => Interlocked.CompareExchange(ref _asked, 1, 0) == 0;

        public void Release() => Volatile.Write(ref _asked, 0);
    }
}
