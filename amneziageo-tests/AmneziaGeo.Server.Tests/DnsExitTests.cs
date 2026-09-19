using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsExitTests
{
    private static readonly OutboundConfig[] Outbounds =
    [
        new() { Name = "direct", Kind = OutboundKind.Local, Mark = 0xA601, Table = OutboundRules.MainTable },
        new() { Name = "myvpn", Kind = OutboundKind.Wg, Mark = 0xA602, Table = 42602 },
        new() { Name = "bor", Kind = OutboundKind.Wg, Mark = 0xA603, Table = 42603 },
        new() { Name = "spare", Kind = OutboundKind.Wg, Mark = 0xA604, Table = 42604, IsEnabled = false },
    ];

    private static RouteWays Ways(IReadOnlySet<string>? alive = null, params Balancer[] balancers) =>
        new(Outbounds, balancers, alive);

    private static DnsSettings Through(string name) => DnsDefaults.Settings with { Outbound = name };

    private static Balancer Mix(string strategy) => new()
    {
        Id = 1,
        Name = "mix",
        Strategy = strategy,
        Members = ["bor", "myvpn"],
    };

    [Fact]
    public void WithoutAChannelTheQuestionsLeaveThroughTheHost()
    {
        var way = DnsExit.Way(DnsDefaults.Settings, Ways());

        Assert.Equal(0u, way.Mark);
        Assert.Null(way.Fault);
    }

    [Fact]
    public void TheQuestionsCarryTheMarkOfTheChannel()
    {
        var way = DnsExit.Way(Through("bor"), Ways());

        Assert.Equal(0xA603u, way.Mark);
        Assert.Equal("bor", way.Outbound);
        Assert.Null(way.Fault);
    }

    [Fact]
    public void AChannelThePanelDoesNotHoldAsksNothing()
    {
        var way = DnsExit.Way(Through("gone"), Ways());

        Assert.Null(way.Mark);
        Assert.Equal("unknown-outbound", way.Fault?.Code);
    }

    [Fact]
    public void TheChannelIsTakenByItsExactName()
    {
        Assert.Null(DnsExit.Way(Through("BOR"), Ways()).Mark);
    }

    [Fact]
    public void AChannelThatIsTurnedOffAsksNothing()
    {
        var way = DnsExit.Way(Through("spare"), Ways());

        Assert.Null(way.Mark);
        Assert.Equal("outbound-off", way.Fault?.Code);
    }

    [Fact]
    public void AChannelThatCarriesNothingIsStillAskedThroughAndSaysSo()
    {
        var way = DnsExit.Way(Through("bor"), Ways(new HashSet<string> { "myvpn" }));

        Assert.Equal(0xA603u, way.Mark);
        Assert.Equal("outbound-down", way.Fault?.Code);
    }

    [Fact]
    public void ABalancerTakesItsFirstMemberWhileBothCarry()
    {
        var way = DnsExit.Way(Through("mix"), Ways(null, Mix(BalanceStrategy.Priority)));

        Assert.Equal(0xA603u, way.Mark);
        Assert.Equal("bor", way.Outbound);
        Assert.Null(way.Fault);
    }

    [Fact]
    public void ABalancerHandsTheQuestionsToItsFirstLiveMember()
    {
        var way = DnsExit.Way(Through("mix"), Ways(new HashSet<string> { "myvpn" }, Mix(BalanceStrategy.Priority)));

        Assert.Equal(0xA602u, way.Mark);
        Assert.Equal("myvpn", way.Outbound);
        Assert.Null(way.Fault);
    }

    [Fact]
    public void ARoundBalancerHandsTheQuestionsOutInTurn()
    {
        var ways = Ways(null, Mix(BalanceStrategy.Round));

        var marks = new long[] { 0, 1, 2 }.Select(turn => DnsExit.Way(Through("mix"), ways, turn).Mark);

        Assert.Equal(new uint?[] { 0xA603, 0xA602, 0xA603 }, marks);
    }

    [Fact]
    public void AStickyBalancerHandsTheResolverItsFirstLiveMember()
    {
        var ways = Ways(null, Mix(BalanceStrategy.Sticky));

        var marks = new long[] { 0, 1, 2 }.Select(turn => DnsExit.Way(Through("mix"), ways, turn).Mark);

        Assert.Equal(new uint?[] { 0xA603, 0xA603, 0xA603 }, marks);
    }

    [Fact]
    public void ABalancerWithNothingLiveStillAsksThroughItsFirstMember()
    {
        var way = DnsExit.Way(Through("mix"), Ways(new HashSet<string>(), Mix(BalanceStrategy.Priority)));

        Assert.Equal(0xA603u, way.Mark);
        Assert.Equal("no-live-member", way.Fault?.Code);
    }

    [Fact]
    public void ABalancerThatIsTurnedOffAsksNothing()
    {
        var way = DnsExit.Way(Through("mix"), Ways(null, Mix(BalanceStrategy.Priority) with { IsEnabled = false }));

        Assert.Null(way.Mark);
        Assert.Equal("balancer-off", way.Fault?.Code);
    }

    [Fact]
    public void ABalancerWithoutAMemberThatIsOnAsksNothing()
    {
        var balancer = Mix(BalanceStrategy.Priority) with { Members = ["spare", "gone"] };

        var way = DnsExit.Way(Through("mix"), Ways(null, balancer));

        Assert.Null(way.Mark);
        Assert.Equal("no-member", way.Fault?.Code);
    }

    [Fact]
    public void ANameIsKnownWhenAnOutboundOrABalancerCarriesIt()
    {
        var balancers = new[] { Mix(BalanceStrategy.Priority) };

        Assert.True(DnsExit.Knows("bor", Outbounds, balancers));
        Assert.True(DnsExit.Knows("mix", Outbounds, balancers));
        Assert.False(DnsExit.Knows("gone", Outbounds, balancers));
    }

    [Fact]
    public async Task AClosedWaySendsNoQuestion()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var closed = new DnsUpstream([$"127.0.0.1:{port}"], TimeSpan.FromMilliseconds(300), () => null);
        var open = new DnsUpstream([$"127.0.0.1:{port}"], TimeSpan.FromMilliseconds(300), () => 0u);

        var answer = await closed.AskAsync(new byte[12], false, CancellationToken.None);
        var before = server.Available;
        await open.AskAsync(new byte[12], false, CancellationToken.None);

        Assert.Null(answer);
        Assert.Equal(0, before);
        Assert.True(server.Available > 0);
    }
}
