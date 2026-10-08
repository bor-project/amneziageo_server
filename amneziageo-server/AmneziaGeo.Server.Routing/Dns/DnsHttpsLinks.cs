using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Asks name servers over HTTPS, with a client of its own for every outbound the questions leave through.
/// </summary>
public sealed class DnsHttpsLinks : IDisposable
{
    private const string Wire = "application/dns-message";

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Life = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<uint, HttpClient> _clients = new();

    private readonly X509Certificate2Collection? _roots;

    private volatile bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsHttpsLinks(X509Certificate2Collection? roots = null)
    {
        _roots = roots;
    }

    /// <summary>
    /// Returns the answer of a name server to a question, or null when the server sent none.
    /// </summary>
    public async Task<byte[]?> AskAsync(DnsUpstreamAddress server, ReadOnlyMemory<byte> question, uint mark, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (question.Length < DnsMessage.HeaderLength)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = new ByteArrayContent(Unnumbered(question)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(Wire);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Wire));
        using var response = await Client(mark).SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"the name server answered {(int)response.StatusCode}"));
        }

        var answer = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (answer.Length < DnsMessage.HeaderLength)
        {
            return null;
        }

        question.Span[..2].CopyTo(answer);

        return answer;
    }

    /// <summary>
    /// Closes the connections of the clients.
    /// </summary>
    public void Dispose()
    {
        _closed = true;
        foreach (var mark in _clients.Keys)
        {
            if (_clients.TryRemove(mark, out var client))
            {
                client.Dispose();
            }
        }
    }

    // Returns the question under the number zero, the one a question over HTTPS carries.
    private static byte[] Unnumbered(ReadOnlyMemory<byte> question)
    {
        var body = question.ToArray();
        body[0] = 0;
        body[1] = 0;

        return body;
    }

    private HttpClient Client(uint mark)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_clients.TryGetValue(mark, out var held))
        {
            return held;
        }

        lock (_clients)
        {
            return _clients.GetOrAdd(mark, Make);
        }
    }

    private HttpClient Make(uint mark)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionIdleTimeout = Idle,
            PooledConnectionLifetime = Life,
            EnableMultipleHttp2Connections = true,
            ConnectCallback = (context, ct) => DialAsync(context.DnsEndPoint, mark, ct),
        };
        if (_roots is not null)
        {
            handler.SslOptions.CertificateChainPolicy = DnsTlsLinks.Trusting(_roots);
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = DnsUpstream.MaxAnswer,
        };
    }

    private static async ValueTask<Stream> DialAsync(DnsEndPoint point, uint mark, CancellationToken ct)
    {
        var address = IPAddress.Parse(point.Host.Trim('[', ']'));
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (!OutboundMark.Put(socket, mark))
            {
                throw new IOException("the mark of the outbound did not take");
            }

            await socket.ConnectAsync(new IPEndPoint(address, point.Port), ct).ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}
