using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsStreamTests
{
    private const string Given = "203.0.113.9";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AQuestionOverTCPGetsTheAnswerOfAPlainServer()
    {
        await using var book = new Book(Answer);
        using var upstream = new DnsUpstream([$"127.0.0.1:{book.Port}"], Wait);

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "tcp.example"), true, CancellationToken.None);

        Assert.NotNull(answer);
        Assert.Equal([Given], DnsMessage.Read(answer)!.Addresses.Select(address => address.ToString()));
        Assert.Null(upstream.Fault);
    }

    [Fact]
    public async Task AnAnswerOverTCPShorterThanAHeaderIsNoAnswer()
    {
        await using var book = new Book(_ => [0x12, 0x34, 0x81, 0x80, 0x00]);
        using var upstream = new DnsUpstream([$"127.0.0.1:{book.Port}"], Wait);

        Assert.Null(await upstream.AskAsync(DnsBuilder.Question(8, "short.example"), true, CancellationToken.None));
    }

    private static byte[] Answer(byte[] question)
    {
        var asked = DnsMessage.Read(question)!;

        return DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, new Told(asked.Question, asked.Type, 300, Given));
    }

    // A name server on the loopback over plain TCP: every question it reads gets what the reply makes of it.
    private sealed class Book : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly Func<byte[], byte[]> _reply;

        private readonly Task _serving;

        /// <summary>
        /// ctor
        /// </summary>
        public Book(Func<byte[], byte[]> reply)
        {
            _reply = reply;
            _listener.Start();
            _serving = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _serving;
        }

        private async Task ServeAsync()
        {
            while (await AcceptAsync() is { } client)
            {
                using (client)
                {
                    await TalkAsync(client.GetStream());
                }
            }
        }

        private async Task<TcpClient?> AcceptAsync()
        {
            try
            {
                return await _listener.AcceptTcpClientAsync();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return null;
            }
        }

        // Reads one question and writes back the reply, the length ahead of it.
        private async Task TalkAsync(NetworkStream stream)
        {
            try
            {
                var head = new byte[2];
                await stream.ReadExactlyAsync(head);
                var question = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
                await stream.ReadExactlyAsync(question);
                var answer = _reply(question);
                BinaryPrimitives.WriteUInt16BigEndian(head, (ushort)answer.Length);
                await stream.WriteAsync(head);
                await stream.WriteAsync(answer);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
            }
        }
    }
}
