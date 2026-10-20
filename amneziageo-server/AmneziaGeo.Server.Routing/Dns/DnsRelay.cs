using System.Net;
using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Passes a question on to the name server a client asked.
/// </summary>
public interface IDnsRelay
{
    /// <summary>
    /// Returns the answer of the name server, or null when it gave none in time.
    /// </summary>
    Task<byte[]?> AskAsync(IPAddress server, ReadOnlyMemory<byte> question, bool stream, CancellationToken ct);
}

/// <summary>
/// Passes a question on to a name server over plain DNS, the way the host sends its own traffic.
/// </summary>
public sealed class DnsRelay : IDnsRelay
{
    private readonly TimeSpan _wait;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsRelay(TimeSpan wait) => _wait = wait;

    /// <inheritdoc/>
    public async Task<byte[]?> AskAsync(IPAddress server, ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_wait);
        var point = new IPEndPoint(server, DnsDefaults.Port);
        try
        {
            return stream
                ? await DnsUpstream.StreamAsync(point, question, 0, limit.Token).ConfigureAwait(false)
                : await DnsUpstream.PacketAsync(point, question, 0, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            return null;
        }
    }
}
