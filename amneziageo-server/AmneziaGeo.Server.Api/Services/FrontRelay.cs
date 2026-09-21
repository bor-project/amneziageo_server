using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Hands the websocket of a tunnel to the front of its endpoint on the loopback and carries it both ways.
/// </summary>
public static class FrontRelay
{
    private const int MaxHead = 16 * 1024;

    private const int Switching = StatusCodes.Status101SwitchingProtocols;

    private static readonly TimeSpan DialFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns the upgrade request the front on the loopback reads, without the token of the client.
    /// </summary>
    public static string Head(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = new StringBuilder();
        text.Append(request.Method).Append(' ').Append(request.PathBase).Append(request.Path).Append(request.QueryString)
            .Append(" HTTP/1.1\r\n");
        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, HeaderNames.Authorization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in header.Value)
            {
                text.Append(header.Key).Append(": ").Append(value).Append("\r\n");
            }
        }

        text.Append("\r\n");

        return text.ToString();
    }

    /// <summary>
    /// Returns the status of an answer head, zero when the head is not one.
    /// </summary>
    public static int Status(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        var line = head.Split("\r\n", 2)[0].Split(' ', 3);

        return line.Length >= 2 && int.TryParse(line[1], NumberStyles.None, CultureInfo.InvariantCulture, out var status)
            ? status
            : 0;
    }

    /// <summary>
    /// Returns the headers of an answer head.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Headers(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        foreach (var line in head.Split("\r\n").Skip(1))
        {
            var mark = line.IndexOf(':', StringComparison.Ordinal);
            if (mark > 0)
            {
                yield return new KeyValuePair<string, string>(line[..mark].Trim(), line[(mark + 1)..].Trim());
            }
        }
    }

    /// <summary>
    /// Opens the websocket of a request on the front at a loopback port and carries its bytes until either side ends.
    /// </summary>
    public static async Task PassAsync(HttpContext context, int port, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var ct = context.RequestAborted;
        using var backend = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using var dial = CancellationTokenSource.CreateLinkedTokenSource(ct);
            dial.CancelAfter(DialFor);
            await backend.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), dial.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            logger.LogWarning("the websocket front on port {Port} does not answer: {Reason}", port, ex.Message);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;

            return;
        }

        using var stream = new NetworkStream(backend, ownsSocket: false);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(Head(context.Request)), ct).ConfigureAwait(false);
        var (head, tail) = await ReadHeadAsync(stream, ct).ConfigureAwait(false);
        var status = head is null ? 0 : Status(head);
        if (status != Switching)
        {
            context.Response.StatusCode = status > 0 ? status : StatusCodes.Status502BadGateway;

            return;
        }

        foreach (var header in Headers(head!))
        {
            if (!string.Equals(header.Key, HeaderNames.Connection, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.Append(header.Key, header.Value);
            }
        }

        var upgrade = context.Features.Get<IHttpUpgradeFeature>()!;
        var client = await upgrade.UpgradeAsync().ConfigureAwait(false);
        if (tail.Length > 0)
        {
            await client.WriteAsync(tail, ct).ConfigureAwait(false);
        }

        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = CarryAsync(client, stream, done.Token);
        var down = CarryAsync(stream, client, done.Token);
        await Task.WhenAny(up, down).ConfigureAwait(false);
        await done.CancelAsync().ConfigureAwait(false);
        backend.Close();
        await Task.WhenAll(up, down).ConfigureAwait(false);
    }

    // Copies one direction until it ends, whatever ends it.
    private static async Task CarryAsync(Stream from, Stream to, CancellationToken ct)
    {
        try
        {
            await from.CopyToAsync(to, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    // Reads the head of the answer and whatever came after it in the same reads.
    private static async Task<(string? Head, byte[] Tail)> ReadHeadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHead];
        var used = 0;
        while (used < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(used), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return (null, []);
            }

            used += read;
            var end = buffer.AsSpan(0, used).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                return (Encoding.ASCII.GetString(buffer, 0, end), buffer[(end + 4)..used]);
            }
        }

        return (null, []);
    }
}
