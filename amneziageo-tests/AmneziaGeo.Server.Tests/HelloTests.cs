using AmneziaGeo.Server.Api.Hello;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Tests;

public class HelloTests
{
    [Fact]
    public void BothSidesCountTheSameAnswerToAChallenge()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var challenge = PeerProof.Challenge();

        var byClient = PeerProof.Answer(client.PrivateKey, server.PublicKey, challenge);
        var byServer = PeerProof.Answer(server.PrivateKey, client.PublicKey, challenge);

        Assert.Equal(byServer, byClient);
        Assert.True(PeerProof.Holds(server.PrivateKey, client.PublicKey, challenge, byClient));
    }

    [Fact]
    public void AnAnswerOfAnotherKeyOrAnotherChallengeDoesNotHold()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var stranger = Curve25519.Create();
        var challenge = PeerProof.Challenge();
        var answer = PeerProof.Answer(client.PrivateKey, server.PublicKey, challenge);

        Assert.False(PeerProof.Holds(server.PrivateKey, stranger.PublicKey, challenge, answer));
        Assert.False(PeerProof.Holds(server.PrivateKey, client.PublicKey, PeerProof.Challenge(), answer));
        Assert.False(PeerProof.Holds(server.PrivateKey, client.PublicKey, challenge, null));
        Assert.False(PeerProof.Holds(server.PrivateKey, client.PublicKey, challenge, "not an answer"));
    }

    [Fact]
    public void AChallengeIsTakenOnceAndOnlyWhileItStands()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-09-12T12:00:00Z", null));
        var tickets = new SpeedTickets(clock);
        var challenge = tickets.Challenge();

        Assert.True(tickets.Takes(challenge));
        Assert.False(tickets.Takes(challenge));
        Assert.False(tickets.Takes("never handed out"));

        var second = tickets.Challenge();
        clock.Pass(SpeedTickets.ChallengeLife + TimeSpan.FromSeconds(1));
        Assert.False(tickets.Takes(second));
    }

    [Fact]
    public void APassAnswersUntilItRunsOut()
    {
        var clock = new Clock(DateTimeOffset.Parse("2026-09-12T12:00:00Z", null));
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
    public void BothSidesCountTheSameCountersignOfABody()
    {
        var server = Curve25519.Create();
        var client = Curve25519.Create();
        var nonce = Convert.ToBase64String(new byte[PeerProof.NonceBytes]);
        var body = "{\"features\":{}}"u8.ToArray();

        var byServer = PeerProof.Countersign(server.PrivateKey, client.PublicKey, nonce, body);
        var byClient = PeerProof.Countersign(client.PrivateKey, server.PublicKey, nonce, body);

        Assert.Equal(byServer, byClient);
        Assert.NotEqual(byServer, PeerProof.Countersign(server.PrivateKey, client.PublicKey, nonce, "{}"u8));
        Assert.NotEqual(byServer, PeerProof.Countersign(server.PrivateKey, client.PublicKey, PeerProof.Challenge(), body));
        Assert.NotEqual(byServer, PeerProof.Answer(server.PrivateKey, client.PublicKey, nonce));
    }

    [Fact]
    public void ANonceIsThirtyTwoBytesInBase64()
    {
        Assert.True(PeerProof.IsNonce(Convert.ToBase64String(new byte[32])));
        Assert.False(PeerProof.IsNonce(Convert.ToBase64String(new byte[16])));
        Assert.False(PeerProof.IsNonce(Convert.ToBase64String(new byte[33])));
        Assert.False(PeerProof.IsNonce("not base64"));
        Assert.False(PeerProof.IsNonce(null));
    }

    [Fact]
    public void ThePointAnswersOnTheAddressesOfTheEnabledInterfacesAtTheirPorts()
    {
        var one = Endpoint("awg0", 51820, "10.9.0.1/24", "fd00:9::1/64");
        var two = Endpoint("awg1", 51821, "10.9.1.1/24");
        var off = Endpoint("awg2", 51822, "10.9.2.1/24") with { IsEnabled = false };

        Assert.Equal(
            ["10.9.0.1:51820", "[fd00:9::1]:51820", "10.9.1.1:51821"],
            HelloPoints.Of([one, two, off], 0).Select(point => point.ToString()).ToArray());
        Assert.Equal(
            ["10.9.0.1:9443", "[fd00:9::1]:9443", "10.9.1.1:9443"],
            HelloPoints.Of([one, two, off], 9443).Select(point => point.ToString()).ToArray());
    }

    private static ServerConfig Endpoint(string name, int port, params string[] address) => new()
    {
        Name = name,
        ListenPort = port,
        Address = address,
    };
}
