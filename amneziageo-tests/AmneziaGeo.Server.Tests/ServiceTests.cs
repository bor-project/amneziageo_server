using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace AmneziaGeo.Server.Tests;

public class ServiceTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void BothSidesCountTheSameProofOfAToken()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var stranger = Curve25519.Create();
        var nonce = PeerToken.Nonce();
        var byClient = PeerToken.Shared(client.PrivateKey, server.PublicKey);
        var byServer = PeerToken.Shared(server.PrivateKey, client.PublicKey);
        var proof = PeerToken.Proof(byClient, client.PublicKey, 1000, nonce);

        Assert.Equal(byServer, byClient);
        Assert.True(PeerToken.Holds(byServer, client.PublicKey, 1000, nonce, proof));
        Assert.False(PeerToken.Holds(PeerToken.Shared(server.PrivateKey, stranger.PublicKey), client.PublicKey, 1000, nonce, proof));
        Assert.False(PeerToken.Holds(byServer, client.PublicKey, 1001, nonce, proof));
        Assert.False(PeerToken.Holds(byServer, client.PublicKey, 1000, PeerToken.Nonce(), proof));
        Assert.False(PeerToken.Holds(byServer, client.PublicKey, 1000, nonce, null));
    }

    [Fact]
    public void ASealedAnswerOpensUnderItsSecretAndNonceAlone()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var shared = PeerToken.Shared(server.PrivateKey, client.PublicKey);
        var nonce = PeerToken.Nonce();
        var body = "{\"features\":{}}"u8.ToArray();

        var answer = PeerToken.Seal(shared, nonce, body);

        Assert.Equal(body, PeerToken.Open(PeerToken.Shared(client.PrivateKey, server.PublicKey), nonce, answer));
        Assert.Null(PeerToken.Open(shared, PeerToken.Nonce(), answer));
        Assert.Null(PeerToken.Open(PeerToken.Shared(server.PrivateKey, Curve25519.Create().PublicKey), nonce, answer));
        Assert.Null(PeerToken.Open(shared, nonce, answer with { Data = Convert.ToBase64String(new byte[20]) }));
        Assert.Null(PeerToken.Open(shared, nonce, answer with { Iv = "not base64" }));
        Assert.DoesNotContain("features", Encoding.UTF8.GetString(Convert.FromBase64String(answer.Data)), StringComparison.Ordinal);
    }

    [Fact]
    public void ANonceIsSixteenBytesInBase64()
    {
        Assert.True(PeerToken.IsNonce(PeerToken.Nonce()));
        Assert.False(PeerToken.IsNonce(Convert.ToBase64String(new byte[32])));
        Assert.False(PeerToken.IsNonce("not base64"));
        Assert.False(PeerToken.IsNonce(null));
    }

    [Fact]
    public void ATokenStandsFiveMinutesEitherWayOfTheClock()
    {
        var now = DateTimeOffset.Parse("2026-09-21T12:00:00Z", null);
        var time = now.ToUnixTimeSeconds();

        Assert.True(PeerToken.Fresh(time, now));
        Assert.True(PeerToken.Fresh(time - 300, now));
        Assert.True(PeerToken.Fresh(time + 300, now));
        Assert.False(PeerToken.Fresh(time - 301, now));
        Assert.False(PeerToken.Fresh(time + 301, now));
    }

    [Fact]
    public void TheNonceOfATokenIsTakenOnce()
    {
        var tickets = new SpeedTickets();
        var nonce = PeerToken.Nonce();

        Assert.True(tickets.Takes(nonce));
        Assert.False(tickets.Takes(nonce));
        Assert.True(tickets.Takes(PeerToken.Nonce()));
    }

    [Fact]
    public void APassAnswersUntilItRunsOut()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-09-21T12:00:00Z", null));
        var tickets = new SpeedTickets(clock);

        var ticket = tickets.Mint(7);

        Assert.Equal(7, ticket.ClientId);
        Assert.NotNull(tickets.Find(ticket.Value));
        clock.Pass(SpeedTickets.TicketLife + TimeSpan.FromSeconds(1));
        Assert.Null(tickets.Find(ticket.Value));
    }

    [Fact]
    public void AFreshPassOfAClientDropsTheOneItHeld()
    {
        var tickets = new SpeedTickets();
        var held = tickets.Mint(7);

        var fresh = tickets.Mint(7);

        Assert.Null(tickets.Find(held.Value));
        Assert.NotNull(tickets.Find(fresh.Value));
    }

    [Fact]
    public void APassMeasuresOneLegAtATime()
    {
        var tickets = new SpeedTickets();
        var ticket = tickets.Mint(7);

        Assert.True(tickets.Enter(ticket.Value));
        Assert.False(tickets.Enter(ticket.Value));
        tickets.Leave(ticket.Value);
        Assert.True(tickets.Enter(ticket.Value));
    }

    [Fact]
    public void TheServicesOfEveryEnabledEndpointAnswerOnAPortOfTheirOwn()
    {
        var one = Endpoint(1, "awg0", 51820);
        var moved = Endpoint(2, "awg1", 51821) with { ServicesPort = 8443, WebSocket = true };
        var off = Endpoint(3, "awg2", 51822) with { IsEnabled = false };
        var clash = Endpoint(4, "awg3", 51823) with { ServicesPort = 51820 };

        var points = ServicePoints.Of([one, moved, off, clash], "/tls/chain.pem", "/tls/key.pem");

        Assert.Equal(
            [("awg0", 51820, false, 61001, 51820), ("awg1", 8443, true, 61002, 51821)],
            points.Select(point => (point.Name, point.Port, point.WebSocket, point.Front, point.Target)).ToArray());
        Assert.All(points, point => Assert.Equal("/tls/chain.pem", point.Chain));
    }

    [Fact]
    public void TheTokenOfAWebSocketIsReadOutOfItsHeader()
    {
        var token = new HelloRequest("key", 1000, "nonce", "proof");
        var text = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(token, Web)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(token, ServiceDesk.Token("AmneziaGeo " + text, Web));
        Assert.Null(ServiceDesk.Token("Bearer " + text, Web));
        Assert.Null(ServiceDesk.Token("AmneziaGeo not a token", Web));
        Assert.Null(ServiceDesk.Token(null, Web));
    }

    [Fact]
    public void TheHeaderOfAWebSocketCarriesATokenTheServerHolds()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var token = ServiceDesk.Token(PeerToken.Header(client.PrivateKey, server.PublicKey, now), Web);

        Assert.NotNull(token);
        Assert.Equal(client.PublicKey, token.Key);
        Assert.Equal(now.ToUnixTimeSeconds(), token.Time);
        Assert.True(PeerToken.IsNonce(token.Nonce));
        Assert.True(PeerToken.Holds(PeerToken.Shared(server.PrivateKey, client.PublicKey), token.Key!, token.Time, token.Nonce!, token.Proof));
    }

    [Fact]
    public void TheFrontOnTheLoopbackReadsTheUpgradeWithoutTheToken()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/v1/events";
        context.Request.Headers.Host = "vpn.example:51820";
        context.Request.Headers.Upgrade = "websocket";
        context.Request.Headers.Authorization = "AmneziaGeo secret";
        context.Request.Headers["Sec-WebSocket-Protocol"] = "v1, authorization.bearer.jwt";

        var head = FrontRelay.Head(context.Request);

        Assert.StartsWith("GET /v1/events HTTP/1.1\r\n", head, StringComparison.Ordinal);
        Assert.Contains("Upgrade: websocket\r\n", head, StringComparison.Ordinal);
        Assert.Contains("Sec-WebSocket-Protocol: v1, authorization.bearer.jwt\r\n", head, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", head, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\n", head, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAnswerOfTheFrontIsReadForItsStatusAndHeaders()
    {
        const string head = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: abc=";

        Assert.Equal(101, FrontRelay.Status(head));
        Assert.Equal(0, FrontRelay.Status("nonsense"));
        Assert.Equal(
            [new KeyValuePair<string, string>("Upgrade", "websocket"), new KeyValuePair<string, string>("Sec-WebSocket-Accept", "abc=")],
            FrontRelay.Headers(head).ToArray());
    }

    [Fact]
    public async Task AClientThatProvesItsKeyReadsItsFeaturesSealed()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Host = "vpn.example", Address = ["10.8.0.1/24"], WebSocket = true },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"], Routing = ClientRouting.Off },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var token = Token(client.PrivateKey, endpoint.PublicKey, bench.Clock.GetUtcNow());

        var answer = await AskAsync(desk, Point(endpoint), token);

        Assert.Equal(StatusCodes.Status200OK, answer.Status);
        var opened = PeerToken.Open(
            PeerToken.Shared(client.PrivateKey, endpoint.PublicKey),
            token.Nonce!,
            JsonSerializer.Deserialize<SealedAnswer>(answer.Body, Web)!);
        Assert.NotNull(opened);
        using var document = JsonDocument.Parse(opened);
        var root = document.RootElement;
        var features = root.GetProperty("features");
        Assert.Equal("amneziageo", root.GetProperty("server").GetString());
        Assert.Equal("milena", root.GetProperty("client").GetString());
        Assert.Equal(endpoint.ListenPort, features.GetProperty("websocket").GetProperty("port").GetInt32());
        Assert.False(features.GetProperty("routing").GetProperty("allowed").GetBoolean());
        Assert.StartsWith($"https://10.8.0.1:{endpoint.ListenPort}/api/speed/down?", features.GetProperty("speed").GetProperty("inside").GetProperty("down").GetString(), StringComparison.Ordinal);
        Assert.StartsWith($"https://vpn.example:{endpoint.ListenPort}/api/speed/up?", features.GetProperty("speed").GetProperty("outside").GetProperty("up").GetString(), StringComparison.Ordinal);
        Assert.False(features.TryGetProperty("inbound", out _));
        Assert.False(features.TryGetProperty("routes", out _));
    }

    [Fact]
    public async Task AClientLearnsWhereItsSubscriptionIsAndWhatItHandsOutNow()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Host = "vpn.example", Address = ["10.8.0.1/24"] },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var token = Token(client.PrivateKey, endpoint.PublicKey, bench.Clock.GetUtcNow());

        var answer = await AskAsync(desk, Point(endpoint), token);
        var feed = await ReadAsync(desk, Point(endpoint), "/sub/" + client.SubscriptionId);

        var opened = PeerToken.Open(
            PeerToken.Shared(client.PrivateKey, endpoint.PublicKey),
            token.Nonce!,
            JsonSerializer.Deserialize<SealedAnswer>(answer.Body, Web)!);
        using var document = JsonDocument.Parse(opened!);
        var subscription = document.RootElement.GetProperty("features").GetProperty("subscription");
        var revision = subscription.GetProperty("revision").GetString()!;
        Assert.Equal(
            $"https://vpn.example:{endpoint.ListenPort}/sub/{client.SubscriptionId}",
            subscription.GetProperty("url").GetString());
        Assert.Matches("^[0-9a-f]{32}$", revision);
        Assert.Equal(string.Empty, subscription.GetProperty("pin").GetString());
        Assert.Equal(StatusCodes.Status200OK, feed.Status);
        Assert.Equal("\"" + revision + "\"", feed.Tag);
        Assert.Equal(revision, SubscriptionAnswer.Revision(feed.Body));
    }

    [Fact]
    public async Task ThePortOfTheServicesLeavesTheSubscriptionsToAPortOfTheirOwn()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] }, CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None)).Record!;
        var state = new SubscriptionState();
        var desk = Desk(bench, new SpeedTickets(bench.Clock), state);

        var shared = await ReadAsync(desk, Point(endpoint), "/sub/" + client.SubscriptionId);
        var unknown = await ReadAsync(desk, Point(endpoint), "/sub/nobodyhasthisone");
        state.Current = SubscriptionDefaults.Settings with { Separate = true };
        var apart = await ReadAsync(desk, Point(endpoint), "/sub/" + client.SubscriptionId);
        state.Current = SubscriptionDefaults.Settings with { IsEnabled = false };
        var off = await ReadAsync(desk, Point(endpoint), "/sub/" + client.SubscriptionId);

        Assert.Equal(StatusCodes.Status200OK, shared.Status);
        Assert.Equal(StatusCodes.Status404NotFound, unknown.Status);
        Assert.Equal(StatusCodes.Status404NotFound, apart.Status);
        Assert.Equal(StatusCodes.Status404NotFound, off.Status);
    }

    [Fact]
    public async Task ATokenThatDoesNotHoldIsRefusedWithItsReason()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] }, CancellationToken.None)).Record!;
        var other = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.0.1/24"] },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var now = bench.Clock.GetUtcNow();
        var token = Token(client.PrivateKey, endpoint.PublicKey, now);

        var first = await AskAsync(desk, Point(endpoint), token);
        var replayed = await AskAsync(desk, Point(endpoint), token);
        var stranger = await AskAsync(desk, Point(endpoint), Token(Curve25519.Create().PrivateKey, endpoint.PublicKey, now));
        var stale = await AskAsync(desk, Point(endpoint), Token(client.PrivateKey, endpoint.PublicKey, now.AddMinutes(-6)));
        var elsewhere = await AskAsync(desk, Point(other), Token(client.PrivateKey, endpoint.PublicKey, now));
        var forged = await AskAsync(desk, Point(endpoint), Token(client.PrivateKey, other.PublicKey, now));

        Assert.Equal(StatusCodes.Status200OK, first.Status);
        Assert.Equal("replayed", Error(replayed));
        Assert.Equal("unknown-peer", Error(stranger));
        Assert.Equal("stale-time", Error(stale));
        Assert.Equal(now.ToUnixTimeSeconds(), JsonDocument.Parse(stale.Body).RootElement.GetProperty("time").GetInt64());
        Assert.Equal("unknown-peer", Error(elsewhere));
        Assert.Equal("bad-proof", Error(forged));
    }

    [Fact]
    public async Task AWebSocketWithoutATokenFindsNothing()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature>(new Upgradable());
        context.Request.Method = "GET";
        context.Request.Path = "/v1/events";

        await desk.AnswerAsync(context, Point(endpoint));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static ServiceDesk Desk(Bench bench, SpeedTickets tickets, SubscriptionState? subscriptions = null)
    {
        var state = subscriptions ?? new SubscriptionState();

        return new ServiceDesk(
            bench.Scopes,
            tickets,
            [
                new WebSocketOffer(),
                new RoutingOffer(),
                new InboundOffer(),
                new RoutesOffer(),
                new SpeedOffer(tickets),
                new SubscriptionOffer(state, PanelDefaults.Settings, new WebOptions(), bench.Scopes),
            ],
            state,
            Options.Create(new JsonOptions()),
            NullLogger<ServiceDesk>.Instance);
    }

    private static ServicePoint Point(ServerConfig endpoint) => ServicePoints.Of([endpoint], string.Empty, string.Empty)[0];

    private static HelloRequest Token(string privateKey, string serverKey, DateTimeOffset now)
    {
        var key = Curve25519.PublicOf(privateKey);
        var time = now.ToUnixTimeSeconds();
        var nonce = PeerToken.Nonce();

        return new HelloRequest(key, time, nonce, PeerToken.Proof(PeerToken.Shared(privateKey, serverKey), key, time, nonce));
    }

    private static async Task<(int Status, byte[] Body)> AskAsync(ServiceDesk desk, ServicePoint point, HelloRequest token)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = ServiceDesk.HelloPath;
        context.Request.Host = new HostString("vpn.example", point.Port);
        context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(token, Web));
        var body = new MemoryStream();
        context.Response.Body = body;

        await desk.AnswerAsync(context, point);

        return (context.Response.StatusCode, body.ToArray());
    }

    private static async Task<(int Status, string Body, string Tag)> ReadAsync(ServiceDesk desk, ServicePoint point, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Host = new HostString("vpn.example", point.Port);
        var body = new MemoryStream();
        context.Response.Body = body;

        await desk.AnswerAsync(context, point);

        return (context.Response.StatusCode, Encoding.UTF8.GetString(body.ToArray()), context.Response.Headers.ETag.ToString());
    }

    private static string? Error((int Status, byte[] Body) answer)
    {
        Assert.Equal(StatusCodes.Status403Forbidden, answer.Status);

        return JsonDocument.Parse(answer.Body).RootElement.GetProperty("error").GetString();
    }

    private static ServerConfig Endpoint(long id, string name, int port) => new()
    {
        Id = id,
        Name = name,
        ListenPort = port,
        Address = ["10.8.0.1/24"],
    };

    // A request that says it may be upgraded, without a connection behind it.
    private sealed class Upgradable : Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature
    {
        public bool IsUpgradableRequest => true;

        public Task<Stream> UpgradeAsync() => Task.FromResult<Stream>(new MemoryStream());
    }
}
