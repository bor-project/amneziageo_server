using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsRelayTests
{
    private static readonly OutboundConfig[] Ways =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
    ];

    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true };

    [Fact]
    public void TheConnectionIsLookedUpByTheWayItsAnswersGoBack()
    {
        var request = ConntrackOrigin.Request(
            7,
            new IPEndPoint(IPAddress.Parse("10.8.1.2"), 40123),
            new IPEndPoint(IPAddress.Parse("10.8.1.1"), 53),
            false);

        Assert.Equal(0x0101, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(4)));
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8)));
        Assert.Equal(2, request[16]);
        var reply = NetlinkAttributes.Map(request.AsMemory(20));
        var tuple = NetlinkAttributes.Map(reply[2]);
        var ip = NetlinkAttributes.Map(tuple[1]);
        var proto = NetlinkAttributes.Map(tuple[2]);
        Assert.Equal("10.8.1.1", new IPAddress(ip[1].Span).ToString());
        Assert.Equal("10.8.1.2", new IPAddress(ip[2].Span).ToString());
        Assert.Equal(17, proto[1].Span[0]);
        Assert.Equal(53, BinaryPrimitives.ReadUInt16BigEndian(proto[2].Span));
        Assert.Equal(40123, BinaryPrimitives.ReadUInt16BigEndian(proto[3].Span));
    }

    [Fact]
    public void AConnectionOfIPv6IsLookedUpOverTCPInItsOwnFamily()
    {
        var request = ConntrackOrigin.Request(
            1,
            new IPEndPoint(IPAddress.Parse("fd00::2"), 5000),
            new IPEndPoint(IPAddress.Parse("fd00::1"), 53),
            true);

        var tuple = NetlinkAttributes.Map(NetlinkAttributes.Map(request.AsMemory(20))[2]);
        var ip = NetlinkAttributes.Map(tuple[1]);
        Assert.Equal(10, request[16]);
        Assert.Equal("fd00::1", new IPAddress(ip[3].Span).ToString());
        Assert.Equal("fd00::2", new IPAddress(ip[4].Span).ToString());
        Assert.Equal(6, NetlinkAttributes.Map(tuple[2])[1].Span[0]);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("fd00::53")]
    public void TheAddressTheConnectionWasFirstSentToIsRead(string address)
    {
        var sent = IPAddress.Parse(address);
        var writer = new NetlinkWriter();
        writer.Begin(0x0100, 0, 1, 2, 0);
        var tuple = writer.BeginNested(1);
        var ip = writer.BeginNested(1);
        var six = sent.AddressFamily == AddressFamily.InterNetworkV6;
        writer.PutBytes(six ? (ushort)3 : (ushort)1, (six ? IPAddress.Parse("fd00::2") : IPAddress.Parse("10.8.1.2")).GetAddressBytes());
        writer.PutBytes(six ? (ushort)4 : (ushort)2, sent.GetAddressBytes());
        writer.EndNested(ip);
        writer.EndNested(tuple);
        var bytes = writer.ToArray();

        var read = ConntrackOrigin.Destination([new NetlinkMessage(0x0100, 0, 2, bytes.AsMemory(20))]);

        Assert.Equal(sent, read);
    }

    [Fact]
    public void AnAnswerWithoutTheOriginalDirectionNamesNoAddress() =>
        Assert.Null(ConntrackOrigin.Destination([new NetlinkMessage(0x0100, 0, 2, ReadOnlyMemory<byte>.Empty)]));

    [Fact]
    public async Task TheRuleSelectedDnsFeedsTheSetsAndCache()
    {
        var host = new Ledger();
        var sets = new DnsSets(host);
        var plan = Plan();
        var book = new Book("1.2.3.4");
        using var routes = new DnsRoutes(() => plan, (_, _) => book);
        var resolver = new DnsResolver(routes, new DnsCache(16), sets, () => plan, On, new DnsState(),
            ct => sets.FlushAsync(plan, On.NameLifetime, ct), routes: routes);

        var answer = await resolver.AnswerAsync(DnsBuilder.Question(3, "www.youtube.com"), false, CancellationToken.None).ConfigureAwait(false);
        var cached = await resolver.AnswerAsync(DnsBuilder.Question(4, "www.youtube.com"), false, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(["1.2.3.4"], DnsMessage.Read(answer)!.Addresses.Select(address => address.ToString()));
        Assert.Equal(["1.2.3.4"], DnsMessage.Read(cached)!.Addresses.Select(address => address.ToString()));
        Assert.Equal(1, book.Calls);
        Assert.Contains("add element inet amneziageo_rt n1v4 { 1.2.3.4 timeout 3600s }", host.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheServerUsesTheRuleSelectedDns()
    {
        var plan = Plan();
        using var routes = new DnsRoutes(() => plan, (_, _) => new Book("1.2.3.4"));
        var resolver = new DnsResolver(routes, new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            On, new DnsState(), _ => Task.CompletedTask, routes: routes);
        await using var server = new DnsServer(resolver);
        var port = Free();
        server.Start([IPAddress.Loopback], port);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, port);
        await client.SendAsync(DnsBuilder.Question(5, "www.youtube.com")).ConfigureAwait(false);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await client.ReceiveAsync(wait.Token).ConfigureAwait(false);

        Assert.Equal(["1.2.3.4"], DnsMessage.Read(received.Buffer)!.Addresses.Select(address => address.ToString()));
    }

    [Fact]
    public async Task AnUnmatchedQuestionCannotUseAnotherDnsPolicy()
    {
        var plan = Plan();
        var calls = 0;
        using var routes = new DnsRoutes(() => plan, (_, _) =>
        {
            calls++;

            return new Book("1.2.3.4");
        });
        var resolver = new DnsResolver(routes, new DnsCache(16), new DnsSets(new Ledger()), () => plan,
            On, new DnsState(), _ => Task.CompletedTask, routes: routes);

        var answer = await resolver.AnswerAsync(DnsBuilder.Question(3, "nas.lan"), false, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(DnsMessage.ServerFailure, DnsMessage.Read(answer)!.Code);
        Assert.Equal(0, calls);
    }

    private static RoutePlan Plan() =>
        RoutePlan.Build(
            [RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = ["youtube.com"], Dns = ["1.2.3.4"] }],
            Ways,
            GeoIndex.Load([], new MemoryGeoFiles()),
            ["awg1"],
            On);

    private static int Free()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private sealed class Book(string address) : IDnsUpstream
    {
        public int Calls { get; private set; }

        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            Calls++;
            var asked = DnsMessage.Read(question.Span)!;

            return Task.FromResult<byte[]?>(DnsBuilder.Answer(asked.Id, asked.Question, asked.Type,
                new Told(asked.Question, asked.Type, 300, address)));
        }
    }
}
