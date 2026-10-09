using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Template;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting.Internal;
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
    public void TheEndpointsThatShareAPortAnswerOnItTogether()
    {
        var first = Endpoint(1, "awg0", 51820);
        var moved = Endpoint(2, "awg1", 51821) with { ServicesPort = 8443, WebSocket = true };
        var off = Endpoint(3, "awg2", 51822) with { IsEnabled = false };
        var shared = Endpoint(4, "awg3", 51823) with { ServicesPort = 51820, WebSocket = true };

        var points = ServicePoints.Of([first, moved, off, shared], "/tls/chain.pem", "/tls/key.pem");

        Assert.Equal([51820, 8443], points.Select(point => point.Port).ToArray());
        Assert.Equal(
            [("awg0", false, 61001, 51820), ("awg3", true, 61004, 51823)],
            points[0].Endpoints.Select(one => (one.Name, one.WebSocket, one.Front, one.Target)).ToArray());
        Assert.Equal(
            [("awg1", true, 61002, 51821)],
            points[1].Endpoints.Select(one => (one.Name, one.WebSocket, one.Front, one.Target)).ToArray());
        Assert.Equal("awg0, awg3", points[0].Names);
        Assert.Contains(points[0].Endpoints, one => one.WebSocket);
        Assert.Equal(4, points[0].Of(4)!.ConfigId);
        Assert.Null(points[0].Of(3));
        Assert.All(points, point => Assert.Equal("/tls/chain.pem", point.Chain));
    }

    [Fact]
    public async Task APortThatIsSharedTakesTheHelloOfEveryEndpointBehindIt()
    {
        using var bench = new Bench();
        var first = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Host = "vpn.example", Address = ["10.8.0.1/24"] },
            CancellationToken.None)).Record!;
        var other = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { Host = "vpn.example", ListenPort = 51821, Address = ["10.9.0.1/24"], ServicesPort = first.ListenPort },
            CancellationToken.None)).Record!;
        var mine = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(other.Id, "milena") with { Address = ["10.9.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var point = ServicePoints.Of([first, other], string.Empty, string.Empty)[0];
        var alone = ServicePoints.Of([first], string.Empty, string.Empty)[0];
        var token = Token(mine.PrivateKey, other.PublicKey, bench.Clock.GetUtcNow());

        var shared = await AskAsync(desk, point, token);
        var apart = await AskAsync(desk, alone, Token(mine.PrivateKey, other.PublicKey, bench.Clock.GetUtcNow()));

        Assert.Equal(first.ListenPort, point.Port);
        Assert.Equal(StatusCodes.Status200OK, shared.Status);
        Assert.Equal("unknown-peer", Error(apart));
    }

    [Fact]
    public async Task AWebSocketOfAnEndpointThatTakesNoneFindsNothingOnASharedPort()
    {
        using var bench = new Bench();
        var first = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true },
            CancellationToken.None)).Record!;
        var other = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.0.1/24"], ServicesPort = first.ListenPort },
            CancellationToken.None)).Record!;
        var mine = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(other.Id, "milena") with { Address = ["10.9.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var point = ServicePoints.Of([first, other], string.Empty, string.Empty)[0];
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature>(new Upgradable());
        context.Request.Method = "GET";
        context.Request.Path = $"/{other.WebSocketPath}/events";
        context.Request.Headers.Authorization = PeerToken.Header(mine.PrivateKey, other.PublicKey, bench.Clock.GetUtcNow());

        await desk.AnswerAsync(context, point);

        Assert.NotEqual(first.WebSocketPath, other.WebSocketPath);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task AWebSocketComesUnderThePathOfItsEndpointAlone()
    {
        using var bench = new Bench();
        var fresh = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true },
            CancellationToken.None)).Record!;
        var old = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with
            {
                ListenPort = 51821,
                Address = ["10.9.0.1/24"],
                WebSocket = true,
                WebSocketPath = ConfigServices.OldPath,
                ServicesPort = fresh.ListenPort,
            },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var point = ServicePoints.Of([fresh, old], string.Empty, string.Empty)[0];

        Assert.True(desk.Takes(Upgrading($"/{fresh.WebSocketPath}/events"), point));
        Assert.True(desk.Takes(Upgrading("/v1/events"), point));
        Assert.False(desk.Takes(Upgrading("/v2/events"), point));
        Assert.False(desk.Takes(Upgrading("/v1/events"), Point(fresh)));
        Assert.False(desk.Takes(Asking("GET", $"/{fresh.WebSocketPath}/events"), point));
    }

    [Fact]
    public async Task ThePathOfAWebSocketSaysWhoseFrontItGoesTo()
    {
        using var bench = new Bench();
        var first = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true },
            CancellationToken.None)).Record!;
        var other = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.0.1/24"], WebSocket = true, ServicesPort = first.ListenPort },
            CancellationToken.None)).Record!;
        var mine = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(other.Id, "milena") with { Address = ["10.9.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        using var front = new Front();
        var point = Fronted(Fronted(ServicePoints.Of([first, other], string.Empty, string.Empty)[0], first.Id, front.Port), other.Id, Free());
        var context = Upgrading($"/{first.WebSocketPath}/events");
        context.Request.Headers.Authorization = PeerToken.Header(mine.PrivateKey, other.PublicKey, bench.Clock.GetUtcNow());

        await desk.AnswerAsync(context, point);
        var head = await front.Head.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(first.WebSocketPath, other.WebSocketPath);
        Assert.Equal(other.WebSocketPath, point.Of(other.Id)!.Path);
        Assert.StartsWith($"GET /{first.WebSocketPath}/events HTTP/1.1\r\n", head, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", head, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWebSocketFindsNothingUnderAPathTwoEndpointsOfItsPortShare()
    {
        using var bench = new Bench();
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var first = Endpoint(1, "awg0", 51820) with { WebSocket = true };
        var other = Endpoint(2, "awg1", 51821) with { WebSocket = true, ServicesPort = 51820 };
        var point = ServicePoints.Of([first, other], string.Empty, string.Empty)[0];
        var bare = Upgrading("/v1/events");
        var signed = Upgrading("/v1/events");
        signed.Request.Headers.Authorization = PeerToken.Header(Curve25519.Create().PrivateKey, Curve25519.Create().PublicKey, bench.Clock.GetUtcNow());

        await desk.AnswerAsync(bare, point);
        await desk.AnswerAsync(signed, point);

        Assert.Equal(["v1", "v1"], point.Endpoints.Select(one => one.Path).ToArray());
        Assert.Equal(StatusCodes.Status404NotFound, bare.Response.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, signed.Response.StatusCode);
    }

    [Fact]
    public async Task ThePanelTakesOnlyTheRequestsOfTheServicesOfItsPort()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var point = Point(endpoint);

        Assert.True(desk.Takes(Asking("POST", ServiceDesk.HelloPath), point));
        Assert.True(desk.Takes(Asking("GET", ServiceDesk.SpeedPath + "/down"), point));
        Assert.True(desk.Takes(Asking("GET", "/sub/" + client.SubscriptionId), point));
        Assert.False(desk.Takes(Asking("GET", "/api/panel"), point));
        Assert.False(desk.Takes(Asking("GET", "/sub/l4kg8s0xq1zc7ab2/"), point));
    }

    [Fact]
    public void TheHeaderOfAWebSocketCarriesATokenAServerOfTheReleasesBeforeHolds()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

        var header = PeerToken.Header(client.PrivateKey, server.PublicKey, now);
        var text = header[(PeerToken.Scheme.Length + 1)..].Replace('-', '+').Replace('_', '/');
        var body = Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));
        var token = JsonSerializer.Deserialize<HelloRequest>(body, Web)!;

        Assert.StartsWith(PeerToken.Scheme + " ", header, StringComparison.Ordinal);
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
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
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
        Assert.Equal(endpoint.WebSocketPath, features.GetProperty("websocket").GetProperty("path").GetString());
        Assert.False(features.TryGetProperty("routing", out _));
        Assert.StartsWith($"https://10.8.0.1:{endpoint.ListenPort}/api/speed/down?", features.GetProperty("speed").GetProperty("inside").GetProperty("down").GetString(), StringComparison.Ordinal);
        Assert.StartsWith($"https://vpn.example:{endpoint.ListenPort}/api/speed/up?", features.GetProperty("speed").GetProperty("outside").GetProperty("up").GetString(), StringComparison.Ordinal);
        Assert.False(features.TryGetProperty("inbound", out _));
        Assert.False(features.TryGetProperty("routes", out _));
        Assert.Equal(DisconnectSignal.Port, features.GetProperty("disconnect").GetProperty("port").GetInt32());
        Assert.Equal(["10.8.0.1"], features.GetProperty("disconnect").GetProperty("from").EnumerateArray().Select(one => one.GetString()));
    }

    [Fact]
    public async Task AClientIsHandedTheGeoSourcesAndTheRoutingListsOfItsTemplate()
    {
        using var bench = new Bench();
        await bench.Geo.AddAsync(
            new GeoSource { Name = "extra", Kind = GeoKind.Site, Url = "https://geo.example/extra.dat" },
            CancellationToken.None);
        var unblock = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "Unblock", Proxy = ["geosite:youtube"], Direct = ["geoip:ru"], AllUdp = true },
            CancellationToken.None)).Record!;
        var ads = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "No ads", Block = ["geosite:category-ads-all"], Full = true },
            CancellationToken.None)).Record!;
        var template = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "phones", AllowedIps = ["0.0.0.0/0"], Presets = [ads.Id, unblock.Id] },
            CancellationToken.None)).Record!;

        var features = await FeaturesAsync(bench, template.Id);

        var sources = features.GetProperty("sources").GetProperty("items").EnumerateArray().ToList();
        var extra = Assert.Single(sources, item => item.GetProperty("name").GetString() == "extra");
        Assert.Equal("geosite", extra.GetProperty("kind").GetString());
        Assert.Equal("https://geo.example/extra.dat", extra.GetProperty("url").GetString());
        var lists = features.GetProperty("presets").GetProperty("lists").EnumerateArray().ToList();
        Assert.Equal(["No ads", "Unblock"], lists.Select(list => list.GetProperty("name").GetString()));
        Assert.Equal(["block|geosite:category-ads-all"], lists[0].GetProperty("rules").EnumerateArray().Select(rule => rule.GetString()));
        Assert.True(lists[0].GetProperty("full").GetBoolean());
        Assert.False(lists[0].GetProperty("allUdp").GetBoolean());
        Assert.Equal(["proxy|geosite:youtube", "direct|geoip:ru"], lists[1].GetProperty("rules").EnumerateArray().Select(rule => rule.GetString()));
        Assert.True(lists[1].GetProperty("allUdp").GetBoolean());
    }

    [Fact]
    public async Task ARoutingListComesWithItsIdentifierItsTimeAndTheNameOfTheConfiguration()
    {
        using var bench = new Bench();
        var first = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "Unblock", Proxy = ["geosite:youtube"] },
            CancellationToken.None)).Record!;
        bench.Clock.Pass(TimeSpan.FromMinutes(5));
        var second = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "No ads", Block = ["geosite:category-ads-all"] },
            CancellationToken.None)).Record!;
        var template = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "phones", AllowedIps = ["0.0.0.0/0"], Presets = [first.Id, second.Id] },
            CancellationToken.None)).Record!;

        var features = await FeaturesAsync(bench, template.Id);

        var lists = features.GetProperty("presets").GetProperty("lists").EnumerateArray().ToList();
        var ids = lists.Select(list => list.GetProperty("id").GetString()!).ToList();
        Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "D", out _), id));
        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(first.UpdatedUtc, lists[0].GetProperty("updated").GetDateTimeOffset());
        Assert.Equal(second.UpdatedUtc, lists[1].GetProperty("updated").GetDateTimeOffset());
        Assert.All(lists, list => Assert.False(list.GetProperty("default").GetBoolean()));
        Assert.All(lists, list => Assert.Equal("vpn.example-awg1-milena", list.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task ARoutingListMarkedDefaultSaysSoUnderTheNameThePanelGivesTheConfiguration()
    {
        using var bench = new Bench();
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { NameTemplate = "{CLIENT} at {INTERFACE}" }, CancellationToken.None);
        var plain = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "No ads", Block = ["geosite:category-ads-all"] },
            CancellationToken.None)).Record!;
        var marked = (await bench.Presets.AddAsync(
            new RoutingPreset { Name = "Unblock", Proxy = ["geosite:youtube"], IsDefault = true },
            CancellationToken.None)).Record!;
        var template = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "phones", AllowedIps = ["0.0.0.0/0"], Presets = [plain.Id, marked.Id] },
            CancellationToken.None)).Record!;

        var features = await FeaturesAsync(bench, template.Id);

        var lists = features.GetProperty("presets").GetProperty("lists").EnumerateArray().ToList();
        Assert.Equal([plain.Uid, marked.Uid], lists.Select(list => list.GetProperty("id").GetString()));
        Assert.Equal([false, true], lists.Select(list => list.GetProperty("default").GetBoolean()));
        Assert.All(lists, list => Assert.Equal("milena at awg1", list.GetProperty("source").GetString()));
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
    public async Task AWebSocketWithoutATokenReachesTheFrontOfTheEndpointItsPathNames()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true, WebSocketPath = ConfigServices.OldPath },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        using var front = new Front();
        var bare = Upgrading("/v1/events");
        bare.Request.Headers["Sec-WebSocket-Protocol"] = "v1, authorization.bearer.jwt";
        var elsewhere = Upgrading("/v2/events");

        await desk.AnswerAsync(bare, Fronted(Point(endpoint), endpoint.Id, front.Port));
        var head = await front.Head.WaitAsync(TimeSpan.FromSeconds(5));
        await desk.AnswerAsync(elsewhere, Point(endpoint));

        Assert.StartsWith("GET /v1/events HTTP/1.1\r\n", head, StringComparison.Ordinal);
        Assert.Contains("Sec-WebSocket-Protocol: v1, authorization.bearer.jwt\r\n", head, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status404NotFound, elsewhere.Response.StatusCode);
    }

    [Fact]
    public async Task AWebSocketThatStaysDownComesBackWithWhy()
    {
        using var bench = new Bench();
        using var held = new TcpListener(System.Net.IPAddress.IPv6Any, 0);
        held.Server.DualMode = true;
        held.Start();
        var busy = ((System.Net.IPEndPoint)held.LocalEndpoint).Port;
        var blocked = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true, ServicesPort = busy },
            CancellationToken.None)).Record!;
        var fallen = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, Address = ["10.9.0.1/24"], WebSocket = true, ServicesPort = Free() },
            CancellationToken.None)).Record!;
        await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg3") with { ListenPort = 51822, Address = ["10.10.0.1/24"], WebSocket = true, ServicesPort = Free() },
            CancellationToken.None);
        await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg4") with { ListenPort = 51823, Address = ["10.11.0.1/24"], ServicesPort = Free() },
            CancellationToken.None);
        var folder = Path.Combine(Path.GetTempPath(), $"amneziageo-fronts-{Guid.NewGuid():N}");
        var server = new ServiceServer(
            bench.Scopes,
            Desk(bench, new SpeedTickets(bench.Clock)),
            new ProxyHost(new Ledger(), new Fronts("awg2"), folder),
            new WebOptions(),
            PanelDefaults.Settings,
            new ServiceShare(),
            NullLogger<ServiceServer>.Instance);
        try
        {
            var faults = await server.SettleAsync(CancellationToken.None);

            Assert.Equal([blocked.Id, fallen.Id], faults.Keys.Order().ToArray());
            Assert.Contains(busy.ToString(CultureInfo.InvariantCulture), faults[blocked.Id], StringComparison.Ordinal);
            Assert.Equal("the front fell over", faults[fallen.Id]);
            Assert.Equal([busy], server.Refused.Keys.ToArray());
            Assert.Contains(busy.ToString(CultureInfo.InvariantCulture), server.Refused[busy], StringComparison.Ordinal);
        }
        finally
        {
            await server.DisposeAsync();
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task AWebSocketCarriedToAFrontEndsWhenThePanelStops()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], WebSocket = true, WebSocketPath = ConfigServices.OldPath },
            CancellationToken.None)).Record!;
        var life = Life();
        var desk = Desk(bench, new SpeedTickets(bench.Clock), life: life);
        using var front = new Front(held: true);
        using var silent = new Silent();

        var carried = desk.AnswerAsync(Upgrading("/v1/events", silent), Fronted(Point(endpoint), endpoint.Id, front.Port));
        await front.Head.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var early = carried.IsCompleted;
        life.StopApplication();
        await carried.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(early);
    }

    [Fact]
    public async Task TheServicesStopThoughTheTimeToStopRanOut()
    {
        using var bench = new Bench();
        var port = Free();
        await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], ServicesPort = port },
            CancellationToken.None);
        var folder = Path.Combine(Path.GetTempPath(), $"amneziageo-fronts-{Guid.NewGuid():N}");
        var server = new ServiceServer(
            bench.Scopes,
            Desk(bench, new SpeedTickets(bench.Clock)),
            new ProxyHost(new Ledger(), new Fronts(string.Empty), folder),
            new WebOptions(),
            PanelDefaults.Settings,
            new ServiceShare(),
            NullLogger<ServiceServer>.Instance);
        try
        {
            await server.SettleAsync(CancellationToken.None);
            var served = Listens(port);
            await server.StopAsync(new CancellationToken(true));

            Assert.True(served);
            Assert.False(Listens(port));
        }
        finally
        {
            await server.DisposeAsync();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task TheSubscriptionsStopThoughTheTimeToStopRanOut()
    {
        using var bench = new Bench();
        var server = new SubscriptionServer(
            bench.Scopes,
            new SubscriptionState(),
            PanelDefaults.Settings,
            new WebOptions(),
            NullLogger<SubscriptionServer>.Instance);
        try
        {
            await server.StartAsync(CancellationToken.None);
            var stopped = server.StopAsync(new CancellationToken(true));
            await stopped;

            Assert.True(stopped.IsCompletedSuccessfully);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    private static ServiceDesk Desk(
        Bench bench,
        SpeedTickets tickets,
        SubscriptionState? subscriptions = null,
        ApplicationLifetime? life = null)
    {
        var state = subscriptions ?? new SubscriptionState();

        return new ServiceDesk(
            bench.Scopes,
            tickets,
            [
                new WebSocketOffer(),
                new InboundOffer(),
                new DisconnectOffer(),
                new SpeedOffer(tickets),
                new SubscriptionOffer(state, PanelDefaults.Settings, new WebOptions(), bench.Scopes),
                new SourcesOffer(bench.Scopes),
                new PresetsOffer(state, PanelDefaults.Settings, new WebOptions(), bench.Scopes),
            ],
            state,
            life ?? Life(),
            Options.Create(new JsonOptions()),
            NullLogger<ServiceDesk>.Instance);
    }

    // Returns the lifetime of a panel that runs until a test stops it.
    private static ApplicationLifetime Life() => new(NullLogger<ApplicationLifetime>.Instance);

    // Asks the hello as a client of the template and returns the features it was offered.
    private static async Task<JsonElement> FeaturesAsync(Bench bench, long template)
    {
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Host = "vpn.example", Address = ["10.8.0.1/24"] },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"], TemplateId = template },
            CancellationToken.None)).Record!;
        var desk = Desk(bench, new SpeedTickets(bench.Clock));
        var token = Token(client.PrivateKey, endpoint.PublicKey, bench.Clock.GetUtcNow());

        var answer = await AskAsync(desk, Point(endpoint), token);
        var opened = PeerToken.Open(
            PeerToken.Shared(client.PrivateKey, endpoint.PublicKey),
            token.Nonce!,
            JsonSerializer.Deserialize<SealedAnswer>(answer.Body, Web)!);
        using var document = JsonDocument.Parse(opened!);

        return document.RootElement.GetProperty("features").Clone();
    }

    private static ServicePoint Point(ServerConfig endpoint) => ServicePoints.Of([endpoint], string.Empty, string.Empty)[0];

    // Returns the port with the front of one of its endpoints moved to another loopback port.
    private static ServicePoint Fronted(ServicePoint point, long id, int port) =>
        point with { Endpoints = [.. point.Endpoints.Select(one => one.ConfigId == id ? one with { Front = port } : one)] };

    private static DefaultHttpContext Upgrading(string path, Stream? connection = null)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature>(new Upgradable(connection));
        context.Request.Method = "GET";
        context.Request.Path = path;

        return context;
    }

    private static DefaultHttpContext Asking(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        return context;
    }

    // Tells whether a TCP port of the loopback takes a connection.
    private static bool Listens(int port)
    {
        using var probe = new TcpClient();
        try
        {
            probe.Connect(System.Net.IPAddress.Loopback, port);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int Free()
    {
        using var probe = new TcpListener(System.Net.IPAddress.IPv6Any, 0);
        probe.Server.DualMode = true;
        probe.Start();

        return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
    }

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

    // A request that says it may be upgraded, with the connection given behind it or with none.
    private sealed class Upgradable : Microsoft.AspNetCore.Http.Features.IHttpUpgradeFeature
    {
        private readonly Stream? _connection;

        /// <summary>
        /// ctor
        /// </summary>
        public Upgradable(Stream? connection = null)
        {
            _connection = connection;
        }

        public bool IsUpgradableRequest => true;

        public Task<Stream> UpgradeAsync() => Task.FromResult(_connection ?? new MemoryStream());
    }

    // A connection of a client that stays open and says nothing.
    private sealed class Silent : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }

    // Stands in for the front of an endpoint on the loopback: takes one upgrade, says it switches and keeps the head,
    // and the connection too when held.
    private sealed class Front : IDisposable
    {
        private readonly TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);

        private readonly bool _held;

        private TcpClient? _peer;

        /// <summary>
        /// ctor
        /// </summary>
        public Front(bool held = false)
        {
            _held = held;
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
            Head = TakeAsync();
        }

        public int Port { get; }

        public Task<string> Head { get; }

        public void Dispose()
        {
            _peer?.Dispose();
            _listener.Dispose();
        }

        private async Task<string> TakeAsync()
        {
            var peer = await _listener.AcceptTcpClientAsync();
            _peer = peer;
            var stream = peer.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            await stream.WriteAsync("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n\r\n"u8.ToArray());
            if (!_held)
            {
                peer.Dispose();
            }

            return Encoding.ASCII.GetString(buffer, 0, read);
        }
    }

    // Runs every front but the one named, which falls over.
    private sealed class Fronts : IProxyRunner
    {
        private readonly string _falling;

        /// <summary>
        /// ctor
        /// </summary>
        public Fronts(string falling)
        {
            _falling = falling;
        }

        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StartAsync(string name, CancellationToken ct) =>
            Task.FromResult(name == _falling ? new ProxyState(false, "the front fell over") : ProxyState.Up);

        public Task<ProxyState> StopAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);

        public Task<ProxyState> StateAsync(string name, CancellationToken ct) =>
            Task.FromResult(name == _falling ? ProxyState.Down : ProxyState.Up);
    }
}
