using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsSecureTests
{
    private const string Given = "203.0.113.7";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(400);

    private static readonly Authority Trusted = new();

    [Theory]
    [InlineData("1.1.1.1", DnsTransport.Plain, "1.1.1.1", 53, "")]
    [InlineData("8.8.8.8:5300", DnsTransport.Plain, "8.8.8.8", 5300, "")]
    [InlineData("tls://1.1.1.1", DnsTransport.Tls, "1.1.1.1", 853, "")]
    [InlineData("TLS://8.8.8.8:8853", DnsTransport.Tls, "8.8.8.8", 8853, "")]
    [InlineData("tls://[2606:4700:4700::1111]", DnsTransport.Tls, "2606:4700:4700::1111", 853, "")]
    [InlineData("https://1.1.1.1/dns-query", DnsTransport.Https, "1.1.1.1", 443, "/dns-query")]
    [InlineData("https://1.1.1.1", DnsTransport.Https, "1.1.1.1", 443, "/dns-query")]
    [InlineData("https://1.1.1.1/", DnsTransport.Https, "1.1.1.1", 443, "/dns-query")]
    [InlineData("https://9.9.9.9:5053/dns-query", DnsTransport.Https, "9.9.9.9", 5053, "/dns-query")]
    [InlineData("https://[2606:4700:4700::1111]:8443/ask/me", DnsTransport.Https, "2606:4700:4700::1111", 8443, "/ask/me")]
    public void ANameServerIsReadWithTheWayItIsAsked(string text, DnsTransport transport, string address, int port, string path)
    {
        Assert.True(DnsRules.Server(text, out var server));
        Assert.Equal(transport, server.Transport);
        Assert.Equal(address, server.Point.Address.ToString());
        Assert.Equal(port, server.Point.Port);
        Assert.Equal(path, server.Path);
        Assert.Equal(transport != DnsTransport.Plain, server.IsEncrypted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tls://")]
    [InlineData("https://")]
    [InlineData("tls://dns.google")]
    [InlineData("https://dns.google/dns-query")]
    [InlineData("tls://1.1.1.1/dns-query")]
    [InlineData("https://1.1.1.1/dns-query?x=1")]
    [InlineData("https://1.1.1.1:0/dns-query")]
    [InlineData("quic://1.1.1.1")]
    public void ANameServerThatIsNotAnAddressIsRefused(string text) => Assert.False(DnsRules.Server(text, out _));

    [Fact]
    public void ANameServerOverHttpsIsAskedAtItsAddress()
    {
        Assert.True(DnsRules.Server("https://1.1.1.1", out var four));
        Assert.True(DnsRules.Server("https://[2606:4700:4700::1111]:8443/ask", out var six));

        Assert.Equal("https://1.1.1.1/dns-query", four.Url.AbsoluteUri);
        Assert.Equal("https://[2606:4700:4700::1111]:8443/ask", six.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("tls://1.1.1.1")]
    [InlineData("tls://8.8.8.8:8853")]
    [InlineData("tls://[2606:4700:4700::1111]")]
    [InlineData("https://1.1.1.1/dns-query")]
    [InlineData("https://9.9.9.9:5053/dns-query")]
    [InlineData("https://1.1.1.1")]
    public void ANameServerAskedOverTlsOrHttpsPassesTheRules(string text) =>
        Assert.Null(DnsRules.Check(DnsDefaults.Settings with { Upstreams = [text] }));

    [Fact]
    public void ANameServerNamedByNameIsRefusedByTheRules() =>
        Assert.Equal("bad-upstream", DnsRules.Check(DnsDefaults.Settings with { Upstreams = ["https://dns.google/dns-query"] })?.Code);

    [Fact]
    public void TheNameServersAPanelStartsWithAreAskedEncrypted() =>
        Assert.All(DnsDefaults.Upstreams, one => Assert.Matches("^(https|tls)://", one));

    [Fact]
    public async Task AQuestionOverTlsIsAnsweredByTheNameServer()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.1"));
        using var upstream = Upstream($"tls://127.0.0.1:{book.Port}");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        var read = DnsMessage.Read(answer);
        Assert.NotNull(read);
        Assert.Equal(7, read.Id);
        Assert.Equal(Given, Assert.Single(read.Addresses).ToString());
        Assert.Equal("secret.example", book.Heard);
        Assert.Null(upstream.Fault);
    }

    [Fact]
    public async Task QuestionsOverTlsShareTheConnectionThatAnswered()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.1"));
        using var upstream = Upstream($"tls://127.0.0.1:{book.Port}");

        var first = await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        var second = await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), true, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(2, Number(second));
        Assert.Equal(1, book.Connections);
    }

    [Fact]
    public async Task AConnectionTheNameServerClosedIsOpenedAnew()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.1"));
        using var upstream = Upstream($"tls://127.0.0.1:{book.Port}");
        await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);

        book.HangUp();
        var answer = await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), false, CancellationToken.None);

        Assert.Equal(2, Number(answer));
        Assert.Equal(2, book.Connections);
    }

    [Fact]
    public async Task AnUpstreamThatIsClosedLetsItsConnectionsGo()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.1"));
        var upstream = Upstream($"tls://127.0.0.1:{book.Port}");
        await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        var held = book.Open;

        upstream.Dispose();

        Assert.Equal(1, held);
        Assert.True(await Until(() => book.Open == 0));
    }

    [Fact]
    public async Task ACertificateOfAnotherAddressIsRefused()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.2"));
        using var upstream = Upstream($"tls://127.0.0.1:{book.Port}");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        Assert.Null(answer);
        Assert.Null(book.Heard);
    }

    [Fact]
    public async Task ACertificateOfAnUnknownRootIsRefused()
    {
        using var strange = new Authority();
        await using var book = new TlsBook(strange.Issue("127.0.0.1"));
        using var upstream = Upstream($"tls://127.0.0.1:{book.Port}");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        Assert.Null(answer);
        Assert.Null(book.Heard);
    }

    [Fact]
    public async Task AQuestionOverHttpsIsPostedAndAnswered()
    {
        await using var book = new HttpsBook(Trusted.Issue("127.0.0.1"));
        using var upstream = Upstream($"https://127.0.0.1:{book.Port}/dns-query");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        var read = DnsMessage.Read(answer);
        Assert.NotNull(read);
        Assert.Equal(7, read.Id);
        Assert.Equal(Given, Assert.Single(read.Addresses).ToString());
        Assert.Equal("POST", book.Method);
        Assert.Equal("/dns-query", book.Path);
        Assert.Equal("application/dns-message", book.ContentType);
        Assert.Equal("application/dns-message", book.Accept);
        Assert.Equal("secret.example", book.Heard);
        Assert.Equal(0, book.Number);
    }

    [Fact]
    public async Task QuestionsOverHttpsShareTheConnectionThatAnswered()
    {
        await using var book = new HttpsBook(Trusted.Issue("127.0.0.1"));
        using var upstream = Upstream($"https://127.0.0.1:{book.Port}/dns-query");

        await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        var second = await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), false, CancellationToken.None);

        Assert.Equal(2, Number(second));
        Assert.Equal("two.example", book.Heard);
        Assert.Equal(2, book.Shared);
    }

    [Fact]
    public async Task ANameServerThatRefusesOverHttpsGivesNoAnswer()
    {
        await using var book = new HttpsBook(Trusted.Issue("127.0.0.1")) { Status = 500 };
        using var upstream = Upstream($"https://127.0.0.1:{book.Port}/dns-query");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        Assert.Null(answer);
    }

    [Fact]
    public async Task ACertificateOfAnotherAddressIsRefusedOverHttps()
    {
        await using var book = new HttpsBook(Trusted.Issue("127.0.0.2"));
        using var upstream = Upstream($"https://127.0.0.1:{book.Port}/dns-query");

        var answer = await upstream.AskAsync(DnsBuilder.Question(7, "secret.example"), false, CancellationToken.None);

        Assert.Null(answer);
        Assert.Null(book.Heard);
    }

    [Fact]
    public async Task ANameServerThatGaveNoAnswerIsAskedAfterTheOnesThatAnswer()
    {
        var certificate = Trusted.Issue("127.0.0.1");
        await using var mute = new TlsBook(certificate) { Silent = true };
        await using var book = new TlsBook(certificate);
        using var upstream = new DnsUpstream(
            [$"tls://127.0.0.1:{mute.Port}", $"tls://127.0.0.1:{book.Port}"], Wait, null, Trusted.Roots);

        var first = await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        var second = await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), false, CancellationToken.None);

        Assert.Equal(1, Number(first));
        Assert.Equal(2, Number(second));
        Assert.Equal("one.example", mute.Heard);
        Assert.Equal("two.example", book.Heard);
        Assert.Equal(1, mute.Connections);
        Assert.Null(upstream.Fault);
    }

    [Fact]
    public async Task ASilentNameServerThatHasRestedIsAskedAsideAndComesBack()
    {
        var certificate = Trusted.Issue("127.0.0.1");
        var clock = new Clock(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        await using var first = new TlsBook(certificate) { Silent = true };
        await using var second = new TlsBook(certificate);
        using var upstream = new DnsUpstream(
            [$"tls://127.0.0.1:{first.Port}", $"tls://127.0.0.1:{second.Port}"], Wait, null, Trusted.Roots, clock);
        await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), false, CancellationToken.None);
        var resting = first.Heard;

        first.Silent = false;
        clock.Pass(DnsUpstream.Rest);
        var aside = await upstream.AskAsync(DnsBuilder.Question(3, "three.example"), false, CancellationToken.None);
        var recalled = await Until(() => first.Heard == "three.example");
        var back = await Until(async () =>
        {
            await upstream.AskAsync(DnsBuilder.Question(4, "four.example"), false, CancellationToken.None);

            return first.Heard == "four.example";
        });

        Assert.Equal("one.example", resting);
        Assert.Equal(3, Number(aside));
        Assert.True(recalled);
        Assert.True(back);
    }

    [Fact]
    public async Task QuestionsThatGoUnansweredNameTheFault()
    {
        var nobody = Free();
        using var upstream = new DnsUpstream([$"tls://127.0.0.1:{nobody}"], Short, null, Trusted.Roots);

        await upstream.AskAsync(DnsBuilder.Question(1, "one.example"), false, CancellationToken.None);
        await upstream.AskAsync(DnsBuilder.Question(2, "two.example"), false, CancellationToken.None);
        var early = upstream.Fault;
        await upstream.AskAsync(DnsBuilder.Question(3, "three.example"), false, CancellationToken.None);

        Assert.Null(early);
        Assert.StartsWith($"no name server answers: tls://127.0.0.1:{nobody}: ", upstream.Fault, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerAfterMissesTakesTheFaultAway()
    {
        var certificate = Trusted.Issue("127.0.0.1");
        await using var book = new TlsBook(certificate) { Silent = true };
        using var upstream = new DnsUpstream([$"tls://127.0.0.1:{book.Port}"], Wait, null, Trusted.Roots);
        for (ushort number = 1; number <= DnsUpstream.Misses; number++)
        {
            await upstream.AskAsync(DnsBuilder.Question(number, "one.example"), false, CancellationToken.None);
        }

        var silent = upstream.Fault;
        book.Silent = false;
        var answer = await upstream.AskAsync(DnsBuilder.Question(9, "two.example"), false, CancellationToken.None);

        Assert.NotNull(silent);
        Assert.Equal(9, Number(answer));
        Assert.Null(upstream.Fault);
    }

    [Fact]
    public async Task ANameServerThatHoldsItsAnswerIsLeftWhenTheWaitRunsOut()
    {
        await using var book = new TlsBook(Trusted.Issue("127.0.0.1")) { Stalls = true };
        using var upstream = new DnsUpstream([$"tls://127.0.0.1:{book.Port}"], Short, null, Trusted.Roots);

        for (ushort number = 1; number <= DnsUpstream.Misses; number++)
        {
            Assert.Null(await upstream.AskAsync(DnsBuilder.Question(number, "one.example"), false, CancellationToken.None));
        }

        Assert.EndsWith("it did not answer in 0.4 s", upstream.Fault, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerADatagramDoesNotCarryIsCutDownToItsQuestionAndMarked()
    {
        var question = DnsBuilder.Question(9, "many.example");
        var answer = Many(9, "many.example", 40);

        var cut = DnsMessage.Fit(question, answer);

        var read = DnsMessage.Read(cut);
        Assert.True(answer.Length > DnsMessage.DatagramLength);
        Assert.Equal(question.Length, cut.Length);
        Assert.Equal(0x02, cut[2] & 0x02);
        Assert.NotNull(read);
        Assert.Equal(9, read.Id);
        Assert.True(read.IsResponse);
        Assert.Equal("many.example", read.Question);
        Assert.Empty(read.Answers);
        Assert.Equal(40, DnsMessage.Read(answer)?.Answers.Count);
    }

    [Fact]
    public void AnAskingSideThatNamesALargerDatagramGetsTheAnswerWhole()
    {
        var answer = Many(9, "many.example", 40);

        Assert.Same(answer, DnsMessage.Fit(Sized(DnsBuilder.Question(9, "many.example"), 1232), answer));
        Assert.NotSame(answer, DnsMessage.Fit(Sized(DnsBuilder.Question(9, "many.example"), 600), answer));
    }

    [Fact]
    public async Task TheResolverCutsALargeAnswerForADatagramAndGivesItWholeOverAStream()
    {
        var resolver = new DnsResolver(
            new Wide(40),
            new DnsCache(16),
            new DnsSets(new Ledger()),
            () => null,
            DnsDefaults.Settings with { IsEnabled = true },
            new DnsState(),
            _ => Task.CompletedTask);
        var question = DnsBuilder.Question(9, "many.example");

        var packet = await resolver.AnswerAsync(question, false, CancellationToken.None);
        var stream = await resolver.AnswerAsync(question, true, CancellationToken.None);

        Assert.NotNull(packet);
        Assert.Equal(question.Length, packet.Length);
        Assert.Equal(0x02, packet[2] & 0x02);
        Assert.Equal(40, DnsMessage.Read(stream)?.Answers.Count);
    }

    private static DnsUpstream Upstream(string server) => new([server], Wait, null, Trusted.Roots);

    // Returns the number an answer carries, minus one when there is none.
    private static int Number(byte[]? answer) => DnsMessage.Read(answer)?.Id ?? -1;

    private static byte[] Many(ushort id, string name, int count) =>
        DnsBuilder.Answer(
            id,
            name,
            DnsRecordType.A,
            [.. Enumerable.Range(1, count).Select(last => new Told(name, DnsRecordType.A, 60, string.Create(CultureInfo.InvariantCulture, $"203.0.113.{last}")))]);

    // Returns a question that names the size of the datagram its asking side takes.
    private static byte[] Sized(byte[] question, ushort size)
    {
        var sized = new byte[question.Length + 11];
        question.CopyTo(sized, 0);
        BinaryPrimitives.WriteUInt16BigEndian(sized.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16BigEndian(sized.AsSpan(question.Length + 1), 41);
        BinaryPrimitives.WriteUInt16BigEndian(sized.AsSpan(question.Length + 3), size);

        return sized;
    }

    // Returns a loopback port nobody listens on.
    private static int Free()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private static Task<bool> Until(Func<bool> met) => Until(() => Task.FromResult(met()));

    // Waits until a condition holds, a few seconds at most.
    private static async Task<bool> Until(Func<Task<bool>> met)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await met())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    // A root the tests trust and the certificates of name servers it signs.
    private sealed class Authority : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        private readonly X509Certificate2 _root;

        /// <summary>
        /// ctor
        /// </summary>
        public Authority()
        {
            var request = new CertificateRequest("CN=the root of the resolver tests", _key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            _root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }

        public X509Certificate2Collection Roots => [_root];

        // Returns the certificate of a name server at an address, with its key.
        public X509Certificate2 Issue(string address)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=a name server of the tests", key, HashAlgorithmName.SHA256);
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(IPAddress.Parse(address));
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
            using var issued = request.Create(_root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29), RandomNumberGenerator.GetBytes(8));

            return issued.CopyWithPrivateKey(key);
        }

        public void Dispose()
        {
            _root.Dispose();
            _key.Dispose();
        }
    }

    // A name server that answers every name with forty addresses.
    private sealed class Wide(int count) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            var asked = DnsMessage.Read(question.Span)!;

            return Task.FromResult<byte[]?>(Many(asked.Id, asked.Question, count));
        }
    }

    // A name server on the loopback behind TLS: it hands the questions it reads to what answers them.
    private abstract class Book : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        private readonly X509Certificate2 _certificate;

        private readonly List<SslStream> _open = [];

        private readonly Task _serving;

        private int _connections;

        /// <summary>
        /// ctor
        /// </summary>
        protected Book(X509Certificate2 certificate)
        {
            _certificate = certificate;
            _listener.Start();
            _serving = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Connections => Volatile.Read(ref _connections);

        public string? Heard { get; private set; }

        public int Number { get; private set; } = -1;

        public int Open
        {
            get
            {
                lock (_open)
                {
                    return _open.Count;
                }
            }
        }

        // Closes the connections the name server holds, the way one drops the idle ones.
        public void HangUp()
        {
            lock (_open)
            {
                foreach (var stream in _open)
                {
                    stream.Dispose();
                }

                _open.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            HangUp();
            await _serving;
            _certificate.Dispose();
        }

        protected abstract Task TalkAsync(SslStream stream);

        // Returns the answer to a question, keeping what was asked.
        protected byte[] Answer(byte[] question)
        {
            var asked = DnsMessage.Read(question)!;
            Number = asked.Id;
            Heard = asked.Question;

            return DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, new Told(asked.Question, asked.Type, 300, Given));
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                    _ = HoldAsync(client);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
            }
        }

        private async Task HoldAsync(TcpClient client)
        {
            using var held = client;
            var stream = new SslStream(client.GetStream());
            try
            {
                await stream.AuthenticateAsServerAsync(_certificate);
                lock (_open)
                {
                    _open.Add(stream);
                }

                await TalkAsync(stream);
            }
            catch (Exception ex) when (ex is IOException or AuthenticationException or ObjectDisposedException or SocketException)
            {
            }
            finally
            {
                lock (_open)
                {
                    _open.Remove(stream);
                }

                await stream.DisposeAsync();
            }
        }
    }

    // A name server that takes questions over TLS: a silent one hangs up on a question, one that stalls keeps it.
    private sealed class TlsBook(X509Certificate2 certificate) : Book(certificate)
    {
        public bool Silent { get; set; }

        public bool Stalls { get; init; }

        protected override async Task TalkAsync(SslStream stream)
        {
            var head = new byte[2];
            while (await stream.ReadAtLeastAsync(head, head.Length, false) == head.Length)
            {
                var question = new byte[BinaryPrimitives.ReadUInt16BigEndian(head)];
                await stream.ReadExactlyAsync(question);
                var answer = Answer(question);
                if (Silent)
                {
                    return;
                }

                if (Stalls)
                {
                    continue;
                }

                var packet = new byte[answer.Length + 2];
                BinaryPrimitives.WriteUInt16BigEndian(packet, (ushort)answer.Length);
                answer.CopyTo(packet, 2);
                await stream.WriteAsync(packet);
            }
        }
    }

    // A name server that takes questions over HTTPS.
    private sealed class HttpsBook(X509Certificate2 certificate) : Book(certificate)
    {
        private int _shared;

        public int Status { get; init; } = 200;

        // The most questions one connection heard.
        public int Shared => Volatile.Read(ref _shared);

        public string? Method { get; private set; }

        public string? Path { get; private set; }

        public string? ContentType { get; private set; }

        public string? Accept { get; private set; }

        protected override async Task TalkAsync(SslStream stream)
        {
            var heard = 0;
            while (await RequestAsync(stream) is { } question)
            {
                heard++;
                InterlockedMax(ref _shared, heard);
                var answer = Status == 200 ? Answer(question) : [];
                var head = string.Create(
                    CultureInfo.InvariantCulture,
                    $"HTTP/1.1 {Status} Said\r\nContent-Type: application/dns-message\r\nContent-Length: {answer.Length}\r\n\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(answer);
            }
        }

        private static void InterlockedMax(ref int place, int value)
        {
            var seen = Volatile.Read(ref place);
            while (value > seen)
            {
                var before = Interlocked.CompareExchange(ref place, value, seen);
                if (before == seen)
                {
                    return;
                }

                seen = before;
            }
        }

        // Reads one request and returns its body, or null when the connection ended.
        private async Task<byte[]?> RequestAsync(SslStream stream)
        {
            var text = new StringBuilder();
            var one = new byte[1];
            while (text.Length < 4 || text.ToString(text.Length - 4, 4) != "\r\n\r\n")
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    return null;
                }

                text.Append((char)one[0]);
            }

            var lines = text.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var first = lines[0].Split(' ');
            var headers = lines.Skip(1)
                .Select(line => line.Split(':', 2))
                .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
            Method = first[0];
            Path = first[1];
            ContentType = headers.GetValueOrDefault("Content-Type");
            Accept = headers.GetValueOrDefault("Accept");
            var body = new byte[int.Parse(headers["Content-Length"], CultureInfo.InvariantCulture)];
            await stream.ReadExactlyAsync(body);

            return body;
        }
    }
}
