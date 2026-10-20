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

    private static readonly IPAddress Router = IPAddress.Parse("10.254.253.252");

    private static readonly HostRoute[] Lan =
    [
        new(IPAddress.Any, 0, false),
        new(IPAddress.Parse("10.254.0.0"), 16, false),
        new(IPAddress.Parse("10.253.0.0"), 16, true),
    ];

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
    public async Task AQuestionForAPrivateServerIsAskedOfItAndFeedsTheSets()
    {
        var host = new Ledger();
        var sets = new DnsSets(host);
        var plan = Plan();
        var relay = new Relay("10.1.2.3");
        var resolver = Resolver(sets, plan, relay, ct => sets.FlushAsync(plan, On.NameLifetime, ct));

        var answer = await resolver.AnswerAsync(DnsBuilder.Question(3, "www.youtube.com"), false, null, Router, CancellationToken.None);

        Assert.Equal(["10.1.2.3"], DnsMessage.Read(answer)!.Addresses.Select(address => address.ToString()));
        Assert.Equal([Router], relay.Asked);
        Assert.Contains("add element inet amneziageo_rt n1v4 { 10.1.2.3 timeout 3600s }", host.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAnswerOfAPrivateServerStaysOutOfTheCache()
    {
        var resolver = Resolver(new DnsSets(new Ledger()), Plan(), new Relay("10.1.2.3"), _ => Task.CompletedTask);

        await resolver.AnswerAsync(DnsBuilder.Question(3, "nas.lan"), false, null, Router, CancellationToken.None);
        var later = await resolver.AnswerAsync(DnsBuilder.Question(4, "nas.lan"), false, CancellationToken.None);

        Assert.Equal(["1.2.3.4"], DnsMessage.Read(later)!.Addresses.Select(address => address.ToString()));
    }

    [Fact]
    public async Task APrivateServerThatDoesNotAnswerLeavesTheQuestionToTheResolver()
    {
        var state = new DnsState();
        var resolver = new DnsResolver(
            new Book("1.2.3.4"), new DnsCache(16), new DnsSets(new Ledger()), Plan, On, state, _ => Task.CompletedTask, relay: new Relay(null));

        var answer = await resolver.AnswerAsync(DnsBuilder.Question(3, "nas.lan"), false, null, Router, CancellationToken.None);

        Assert.Equal(["1.2.3.4"], DnsMessage.Read(answer)!.Addresses.Select(address => address.ToString()));
        Assert.Equal(0, state.Failed);
    }

    [Fact]
    public async Task APrivateServerThatGaveNoAnswerIsPassedOverAfterwards()
    {
        var relay = new Relay(null);
        var resolver = Resolver(new DnsSets(new Ledger()), Plan(), relay, _ => Task.CompletedTask);

        await resolver.AnswerAsync(DnsBuilder.Question(3, "nas.lan"), false, null, Router, CancellationToken.None);
        var later = await resolver.AnswerAsync(DnsBuilder.Question(4, "nas.lan"), false, null, Router, CancellationToken.None);

        Assert.Equal(["1.2.3.4"], DnsMessage.Read(later)!.Addresses.Select(address => address.ToString()));
        Assert.Equal([Router], relay.Asked);
    }

    [Theory]
    [InlineData("10.254.253.252", "10.1.2.3")]
    [InlineData("8.8.8.8", "1.2.3.4")]
    [InlineData("127.0.0.1", "1.2.3.4")]
    [InlineData("192.168.77.99", "1.2.3.4")]
    [InlineData("10.253.0.1", "1.2.3.4")]
    public async Task TheServerHandsOnOnlyTheQuestionsAClientSentToAPrivateServerBehindIt(string sent, string expected)
    {
        var resolver = Resolver(new DnsSets(new Ledger()), Plan(), new Relay("10.1.2.3"), _ => Task.CompletedTask);
        await using var server = new DnsServer(resolver, (_, _, _) => IPAddress.Parse(sent), () => Lan);
        var port = Free();
        server.Start([IPAddress.Loopback], port);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Connect(IPAddress.Loopback, port);
        await client.SendAsync(DnsBuilder.Question(5, "www.youtube.com"));
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await client.ReceiveAsync(wait.Token);

        Assert.Equal([expected], DnsMessage.Read(received.Buffer)!.Addresses.Select(address => address.ToString()));
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("100.100.100.100", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("fd12::1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("1.1.1.1", false)]
    [InlineData("2606:4700:4700::1111", false)]
    public void ThePrivateRangesNameTheNetworksBehindTheServer(string address, bool inside) =>
        Assert.Equal(inside, RouteProbe.Within(DnsDefaults.Private, IPAddress.Parse(address)));

    private static RoutePlan Plan() =>
        RoutePlan.Build(
            [RouteDefaults.Fresh("rule") with { Id = 1, Action = RouteAction.Out, Outbound = "direct", Targets = ["youtube.com"] }],
            Ways,
            GeoIndex.Load([], new MemoryGeoFiles()),
            ["awg1"],
            On);

    private static DnsResolver Resolver(DnsSets sets, RoutePlan plan, IDnsRelay relay, Func<CancellationToken, Task> land) =>
        new(new Book("1.2.3.4"), new DnsCache(16), sets, () => plan, On, new DnsState(), land, relay: relay);

    // Returns a loopback port nobody listens on.
    private static int Free()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }

    private static byte[] Told(ReadOnlyMemory<byte> question, string address)
    {
        var asked = DnsMessage.Read(question.Span)!;

        return DnsBuilder.Answer(asked.Id, asked.Question, asked.Type, new Told(asked.Question, asked.Type, 300, address));
    }

    private sealed class Book(string address) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct) =>
            Task.FromResult<byte[]?>(Told(question, address));
    }

    private sealed class Relay(string? address) : IDnsRelay
    {
        public List<IPAddress> Asked { get; } = [];

        public Task<byte[]?> AskAsync(IPAddress server, ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
        {
            Asked.Add(server);

            return Task.FromResult(address is null ? null : Told(question, address));
        }
    }
}
