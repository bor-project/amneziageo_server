using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
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
/// Asks the name servers the settings name, taking the first that answers.
/// </summary>
public sealed class DnsUpstream : IDnsUpstream
{
    /// <summary>
    /// The largest answer a name server may send.
    /// </summary>
    public const int MaxAnswer = 65535;

    private readonly IReadOnlyList<IPEndPoint> _servers;

    private readonly TimeSpan _wait;

    private readonly Func<uint?> _way;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsUpstream(IReadOnlyList<string> servers, TimeSpan wait, Func<uint?>? way = null)
    {
        ArgumentNullException.ThrowIfNull(servers);

        _servers = [.. servers.Select(one => DnsRules.Upstream(one, out var point) ? point : null).OfType<IPEndPoint>()];
        _wait = wait;
        _way = way ?? (() => 0u);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
    {
        if (_way() is not { } mark)
        {
            return null;
        }

        foreach (var server in _servers)
        {
            var answer = await OneAsync(server, question, stream, mark, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                return answer;
            }
        }

        return null;
    }

    private async Task<byte[]?> OneAsync(
        IPEndPoint server, ReadOnlyMemory<byte> question, bool stream, uint mark, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_wait);
        try
        {
            return stream
                ? await StreamAsync(server, question, mark, limit.Token).ConfigureAwait(false)
                : await PacketAsync(server, question, mark, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
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
}
