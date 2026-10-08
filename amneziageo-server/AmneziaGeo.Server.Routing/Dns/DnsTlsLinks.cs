using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Asks name servers over TLS and keeps the connections that answered for the questions that follow.
/// </summary>
public sealed class DnsTlsLinks : IDisposable
{
    /// <summary>
    /// How many idle connections are kept to one name server.
    /// </summary>
    public const int Kept = 8;

    /// <summary>
    /// How long an idle connection is kept.
    /// </summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<(IPEndPoint Server, uint Mark), ConcurrentStack<Link>> _idle = new();

    private readonly X509Certificate2Collection? _roots;

    private readonly TimeProvider _time;

    private volatile bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsTlsLinks(X509Certificate2Collection? roots = null, TimeProvider? time = null)
    {
        _roots = roots;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns what a TLS client checks the certificate of a name server against: its address, under the roots
    /// given or the ones the host trusts.
    /// </summary>
    public static SslClientAuthenticationOptions Options(IPAddress server, X509Certificate2Collection? roots)
    {
        ArgumentNullException.ThrowIfNull(server);

        var options = new SslClientAuthenticationOptions { TargetHost = server.ToString() };
        if (roots is not null)
        {
            options.CertificateChainPolicy = Trusting(roots);
        }

        return options;
    }

    /// <summary>
    /// Returns the policy that trusts the roots given in place of the ones the host trusts.
    /// </summary>
    public static X509ChainPolicy Trusting(X509Certificate2Collection roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        policy.CustomTrustStore.AddRange(roots);

        return policy;
    }

    /// <summary>
    /// Returns the answer of a name server to a question, or null when the server sent none.
    /// </summary>
    public async Task<byte[]?> AskAsync(IPEndPoint server, ReadOnlyMemory<byte> question, uint mark, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ObjectDisposedException.ThrowIf(_closed, this);

        var key = (server, mark);
        while (Take(key) is { } kept)
        {
            var answer = await SpendAsync(kept, question, false, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                Keep(key, kept);

                return answer;
            }
        }

        var fresh = await OpenAsync(server, mark, ct).ConfigureAwait(false);
        var first = await SpendAsync(fresh, question, true, ct).ConfigureAwait(false);
        if (first is not null)
        {
            Keep(key, fresh);
        }

        return first;
    }

    /// <summary>
    /// Closes the connections that are kept.
    /// </summary>
    public void Dispose()
    {
        _closed = true;
        foreach (var held in _idle.Values)
        {
            while (held.TryPop(out var link))
            {
                link.Dispose();
            }
        }
    }

    // Asks over a connection and closes it when it gave no answer; a connection that was kept and turned out to be gone gives null.
    private static async Task<byte[]?> SpendAsync(Link link, ReadOnlyMemory<byte> question, bool fresh, CancellationToken ct)
    {
        try
        {
            var answer = await ExchangeAsync(link.Stream, question, ct).ConfigureAwait(false);
            if (answer is null)
            {
                link.Dispose();
            }

            return answer;
        }
        catch (Exception ex) when (!fresh && (ex is IOException or SocketException or ObjectDisposedException))
        {
            link.Dispose();

            return null;
        }
        catch
        {
            link.Dispose();

            throw;
        }
    }

    private static async Task<byte[]?> ExchangeAsync(SslStream stream, ReadOnlyMemory<byte> question, CancellationToken ct)
    {
        var packet = new byte[question.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(packet, (ushort)question.Length);
        question.CopyTo(packet.AsMemory(2));
        await stream.WriteAsync(packet, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        var head = new byte[2];
        if (await stream.ReadAtLeastAsync(head, head.Length, false, ct).ConfigureAwait(false) < head.Length)
        {
            return null;
        }

        var answer = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
        var read = await stream.ReadAtLeastAsync(answer, answer.Length, false, ct).ConfigureAwait(false);

        return read == answer.Length && read >= DnsMessage.HeaderLength && answer[0] == packet[2] && answer[1] == packet[3]
            ? answer
            : null;
    }

    private async Task<Link> OpenAsync(IPEndPoint server, uint mark, CancellationToken ct)
    {
        var socket = new Socket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (!OutboundMark.Put(socket, mark))
            {
                throw new IOException("the mark of the outbound did not take");
            }

            await socket.ConnectAsync(server, ct).ConfigureAwait(false);

            return await SecureAsync(new SslStream(new NetworkStream(socket, ownsSocket: true)), server.Address, ct).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    private async Task<Link> SecureAsync(SslStream stream, IPAddress server, CancellationToken ct)
    {
        try
        {
            await stream.AuthenticateAsClientAsync(Options(server, _roots), ct).ConfigureAwait(false);

            return new Link(stream, _time.GetUtcNow());
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    // Takes a kept connection that has not idled out, closing the ones that have.
    private Link? Take((IPEndPoint Server, uint Mark) key)
    {
        if (!_idle.TryGetValue(key, out var held))
        {
            return null;
        }

        while (held.TryPop(out var link))
        {
            if (_time.GetUtcNow() - link.Since < Idle)
            {
                return link;
            }

            link.Dispose();
        }

        return null;
    }

    // Keeps a connection for the next question, closing it when enough are kept already.
    private void Keep((IPEndPoint Server, uint Mark) key, Link link)
    {
        var held = _idle.GetOrAdd(key, _ => new ConcurrentStack<Link>());
        if (_closed || held.Count >= Kept)
        {
            link.Dispose();

            return;
        }

        held.Push(new Link(link.Stream, _time.GetUtcNow()));
        if (_closed && held.TryPop(out var late))
        {
            late.Dispose();
        }
    }

    // A connection to a name server and when it was last used.
    private sealed record Link(SslStream Stream, DateTimeOffset Since) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => Stream.Dispose();
    }
}
