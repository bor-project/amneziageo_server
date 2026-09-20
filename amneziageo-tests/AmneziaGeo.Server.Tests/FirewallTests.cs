using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class FirewallTests
{
    [Fact]
    public void OnlyWhatIsTurnedOnAndOpenedGoesIntoThePlan()
    {
        var plan = FirewallPlan.Of(
            [
                Endpoint(),
                Endpoint() with { Name = "awg1", ListenPort = 51821, Opened = false },
                Endpoint() with { Name = "awg2", ListenPort = 51822, IsEnabled = false },
            ],
            [Proxy(), Proxy() with { Name = "proxy1", Port = 8447, Opened = false }],
            new PanelSettings { Port = 8443, Opened = true },
            new SubscriptionSettings { IsEnabled = true, Port = 8444, Opened = true });

        Assert.Equal(
            [("udp", 51820), ("tcp", 8446), ("tcp", 8443), ("tcp", 8444)],
            plan.Ports.Select(port => (port.Protocol, port.Port)).ToArray());
        Assert.Equal(["awg0"], plan.Interfaces);
    }

    [Fact]
    public void SubscriptionsThatAreOffOpenNoPort()
    {
        var plan = FirewallPlan.Of(
            [],
            [],
            new PanelSettings { Port = 8443 },
            new SubscriptionSettings { IsEnabled = false, Port = 8444, Opened = true });

        Assert.Empty(plan.Ports);
    }

    [Fact]
    public void APanelBoundToTheLoopbackAloneOpensNoPort()
    {
        var closed = FirewallPlan.Of(
            [],
            [],
            new PanelSettings { Port = 8443, Opened = true, Listen = ["127.0.0.1", "::1"] },
            new SubscriptionSettings());
        var open = FirewallPlan.Of(
            [],
            [],
            new PanelSettings { Port = 8443, Opened = true, Listen = ["127.0.0.1", "10.0.0.1"] },
            new SubscriptionSettings());

        Assert.Empty(closed.Ports);
        Assert.Equal(8443, Assert.Single(open.Ports).Port);
    }

    [Fact]
    public void ARelayTakesItsPortOverUdp()
    {
        var plan = FirewallPlan.Of(
            [],
            [Proxy() with { Name = "relay0", Kind = ProxyKind.Wg, Port = 8500 }],
            new PanelSettings(),
            new SubscriptionSettings());

        var port = Assert.Single(plan.Ports);
        Assert.Equal("udp", port.Protocol);
        Assert.Equal(8500, port.Port);
    }

    [Fact]
    public void AnEndpointTakesItsPortAndBothWaysThroughItsInterface()
    {
        var wanted = UfwRules.Wanted(FirewallPlan.Of(
            [Endpoint()],
            [],
            new PanelSettings(),
            new SubscriptionSettings()));

        Assert.Equal(
            ["allow 51820/udp", "route allow in on awg0", "route allow out on awg0"],
            wanted.Select(rule => string.Join(" ", rule.Arguments)).ToArray());
        Assert.Equal(["amneziageo awg0"], wanted.Select(rule => rule.Note).Distinct().ToArray());
    }

    [Fact]
    public void TheRulesOfOthersAreLeftWhereTheyAre()
    {
        var held = UfwRules.Read(
            "Added user rules (see 'ufw status' for running firewall):\n"
            + "ufw allow 3389/tcp\n"
            + "ufw allow 22/tcp comment 'ssh of the host'\n"
            + "ufw allow 51820/udp comment 'amneziageo awg0'\n"
            + "ufw route allow in on awg0 comment 'amneziageo awg0'\n");

        Assert.Equal(
            ["allow 51820/udp", "route allow in on awg0"],
            held.Select(rule => string.Join(" ", rule.Arguments)).ToArray());
    }

    [Fact]
    public async Task APortThatIsAlreadyOpenIsNotOpenedAgain()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "ufw allow 51820/udp comment 'amneziageo awg0'\n"
            + "ufw route allow in on awg0 comment 'amneziageo awg0'\n"
            + "ufw route allow out on awg0 comment 'amneziageo awg0'\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.ApplyAsync(
            FirewallPlan.Of([Endpoint()], [], new PanelSettings(), new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal("ufw", sync.Engine);
        Assert.Equal(["ufw show added"], tools.Calls);
    }

    [Fact]
    public async Task APortThatIsNoLongerKeptOpenIsTakenOutOfUfw()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "ufw allow 22/tcp comment 'ssh of the host'\n"
            + "ufw allow 8447/tcp comment 'amneziageo proxy1'\n"
            + "ufw route allow in on awg1 comment 'amneziageo awg1'\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        await host.ApplyAsync(
            FirewallPlan.Of([], [Proxy()], new PanelSettings(), new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(tools.Called("ufw allow 8446/tcp comment amneziageo proxy0"));
        Assert.True(tools.Called("ufw delete allow 8447/tcp"));
        Assert.True(tools.Called("ufw route delete allow in on awg1"));
        Assert.DoesNotContain(tools.Calls, line => line.StartsWith("ufw delete allow 22/tcp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUfwThatRefusesLeavesTheReason()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(1, string.Empty, "ERROR: need root");
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.ApplyAsync(FirewallPlan.None, CancellationToken.None);

        Assert.False(sync.IsDone);
        Assert.Equal("ERROR: need root", sync.Message);
    }

    [Fact]
    public async Task AHostWithNoUfwTakesTheTableOfTheOpenPorts()
    {
        var ledger = new Ledger();
        var host = new FirewallHost(new Tools(), ledger, string.Empty);

        var sync = await host.ApplyAsync(
            FirewallPlan.Of([Endpoint()], [Proxy()], new PanelSettings(), new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal("nft", sync.Engine);
        Assert.Contains("table inet amneziageo_open", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains("udp dport 51820 accept", ledger.Ruleset, StringComparison.Ordinal);
        Assert.DoesNotContain("tcp dport 51820", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains("tcp dport 8446 accept", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains("iifname \"awg0\" accept", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Contains("oifname \"awg0\" accept", ledger.Ruleset, StringComparison.Ordinal);
    }

    private static ServerConfig Endpoint() => new()
    {
        Name = "awg0",
        ListenPort = 51820,
        Address = ["10.0.0.1/24"],
        IsEnabled = true,
        Opened = true,
    };

    private static ProxyConfig Proxy() => new()
    {
        Name = "proxy0",
        Kind = ProxyKind.Ws,
        IsEnabled = true,
        Port = 8446,
        Opened = true,
    };
}
