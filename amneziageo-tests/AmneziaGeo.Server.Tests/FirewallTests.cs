using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
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
            new PanelSettings { Port = 8443, Opened = true },
            new SubscriptionSettings { IsEnabled = true, Separate = true, Port = 8444, Opened = true });

        Assert.Equal(
            [("udp", 51820), ("tcp", 51820), ("tcp", 8443), ("tcp", 8444)],
            plan.Ports.Select(port => (port.Protocol, port.Port)).ToArray());
        Assert.Equal(["awg0"], plan.Interfaces);
    }

    [Fact]
    public void SubscriptionsOnThePortsOfTheServicesOpenNoPortOfTheirOwn()
    {
        var plan = FirewallPlan.Of(
            [Endpoint()],
            new PanelSettings { Port = 8443 },
            new SubscriptionSettings { IsEnabled = true, Port = 8444, Opened = true });

        Assert.Equal(
            [("udp", 51820), ("tcp", 51820)],
            plan.Ports.Select(port => (port.Protocol, port.Port)).ToArray());
    }

    [Fact]
    public void SubscriptionsThatAreOffOpenNoPort()
    {
        var plan = FirewallPlan.Of(
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
            new PanelSettings { Port = 8443, Opened = true, Listen = ["127.0.0.1", "::1"] },
            new SubscriptionSettings());
        var open = FirewallPlan.Of(
            [],
            new PanelSettings { Port = 8443, Opened = true, Listen = ["127.0.0.1", "10.0.0.1"] },
            new SubscriptionSettings());

        Assert.Empty(closed.Ports);
        Assert.Equal(8443, Assert.Single(open.Ports).Port);
    }

    [Fact]
    public void TheServicesOfAnEndpointTakeTheirOwnPortWhereTheyMoved()
    {
        var plan = FirewallPlan.Of([Endpoint() with { ServicesPort = 8446 }], new PanelSettings(), new SubscriptionSettings());

        Assert.Equal(
            [("udp", 51820), ("tcp", 8446)],
            plan.Ports.Select(port => (port.Protocol, port.Port)).ToArray());
    }

    [Fact]
    public void AnEndpointTakesItsPortAndBothWaysThroughItsInterface()
    {
        var wanted = UfwRules.Wanted(FirewallPlan.Of(
            [Endpoint()],
            new PanelSettings(),
            new SubscriptionSettings()));

        Assert.Equal(
            ["allow 51820/udp", "allow 51820/tcp", "route allow in on awg0", "route allow out on awg0"],
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
            + "ufw allow 51820/tcp comment 'amneziageo awg0'\n"
            + "ufw route allow in on awg0 comment 'amneziageo awg0'\n"
            + "ufw route allow out on awg0 comment 'amneziageo awg0'\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.ApplyAsync(
            FirewallPlan.Of([Endpoint()], new PanelSettings(), new SubscriptionSettings()),
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
            FirewallPlan.Of([Endpoint() with { ServicesPort = 8446 }], new PanelSettings(), new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(tools.Called("ufw allow 8446/tcp comment amneziageo awg0"));
        Assert.True(tools.Called("ufw delete allow 8447/tcp"));
        Assert.True(tools.Called("ufw route delete allow in on awg1"));
        Assert.DoesNotContain(tools.Calls, line => line.StartsWith("ufw delete allow 22/tcp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARuleTheHostHoldsAlreadyIsLeftToIt()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "Added user rules (see 'ufw status' for running firewall):\n"
            + "ufw allow 51820/udp comment 'amneziawg inbound 6'\n"
            + "ufw allow 8443/tcp\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.ApplyAsync(
            FirewallPlan.Of([Endpoint()], new PanelSettings { Opened = true }, new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.False(tools.Called("ufw allow 51820/udp"));
        Assert.False(tools.Called("ufw allow 8443/tcp"));
        Assert.True(tools.Called("ufw allow 51820/tcp comment amneziageo awg0"));
        Assert.True(tools.Called("ufw route allow in on awg0 comment amneziageo awg0"));
    }

    [Fact]
    public async Task ARuleTheHostHoldsIsNotTakenOutWhenThePortCloses()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "ufw allow 51820/udp comment 'amneziawg inbound 6'\n"
            + "ufw allow 8443/tcp\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        await host.ApplyAsync(FirewallPlan.None, CancellationToken.None);

        Assert.Equal(["ufw show added"], tools.Calls);
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
            FirewallPlan.Of([Endpoint() with { ServicesPort = 8446 }], new PanelSettings(), new SubscriptionSettings()),
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

    [Fact]
    public async Task AUfwThatShowsItsRulesLetsThePanelChangeIt()
    {
        var tools = new Tools();
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var reach = await host.ReachAsync(CancellationToken.None);

        Assert.Equal(new FirewallReach("ufw", true, string.Empty, string.Empty), reach);
        Assert.Equal(["ufw show added"], tools.Calls);
    }

    [Fact]
    public async Task AUfwThatRefusesToShowItsRulesLeavesThePanelWithoutTheRights()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(1, string.Empty, "ERROR: You need to be root to run this script\n");
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var reach = await host.ReachAsync(CancellationToken.None);

        Assert.Equal(
            new FirewallReach("ufw", false, FirewallReach.NoRights, "ERROR: You need to be root to run this script"),
            reach);
    }

    [Fact]
    public async Task AUfwOfTheHostTheContainerDoesNotReachStandsInTheWay()
    {
        var tools = new Tools();
        tools.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(
            0,
            "table ip filter {\n\tchain ufw-user-input {\n\t}\n}\n",
            string.Empty);
        var ledger = new Ledger();
        var host = new FirewallHost(tools, ledger, string.Empty);

        var reach = await host.ReachAsync(CancellationToken.None);

        Assert.Equal("nft", reach.Engine);
        Assert.False(reach.IsAble);
        Assert.Equal(FirewallReach.HostUfw, reach.Reason);
        Assert.NotEmpty(reach.Message);
        Assert.Empty(ledger.Steps);
    }

    [Fact]
    public async Task AHostThatRefusesTheDryRunOfTheTableLeavesThePanelWithoutTheRights()
    {
        var tools = new Tools();
        tools.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var ledger = new Ledger { Refuses = "check", Complaint = "Operation not permitted" };
        var host = new FirewallHost(tools, ledger, string.Empty);

        var reach = await host.ReachAsync(CancellationToken.None);

        Assert.Equal(new FirewallReach("nft", false, FirewallReach.NoRights, "'check' was refused: Operation not permitted"), reach);
    }

    [Fact]
    public async Task AHostWithoutUfwThatTakesTheDryRunLetsThePanelChangeIt()
    {
        var tools = new Tools();
        tools.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var ledger = new Ledger();
        var host = new FirewallHost(tools, ledger, string.Empty);

        var reach = await host.ReachAsync(CancellationToken.None);

        Assert.Equal(FirewallReach.Able("nft"), reach);
        Assert.Equal(["check"], ledger.Steps);
        Assert.Equal(OpenRuleset.Text(FirewallPlan.None), ledger.Checked);
        Assert.Empty(ledger.Ruleset);
    }

    [Fact]
    public void ARuleHandedOverKeepsTheArgumentsOfThePanelUnderACommentWithoutItsMark()
    {
        var held = UfwRules.Read(
            "ufw allow 51820/udp comment 'amneziageo awg0'\n"
            + "ufw allow in on awg0 to any port 53 proto udp comment 'amneziageo dns'\n"
            + "ufw allow 22/tcp comment 'ssh of the host'\n");

        var kept = UfwRules.Kept(
            [new FirewallPort("udp", 51820, "awg0"), new FirewallPort("udp", 53, "dns", "awg0"), new FirewallPort("tcp", 22, "ssh")],
            held);

        Assert.Equal(
            ["allow 51820/udp comment kept by amneziageo awg0", "allow in on awg0 to any port 53 proto udp comment kept by amneziageo dns"],
            kept.Select(rule => string.Join(" ", UfwRules.Add(rule))).ToArray());
        Assert.Empty(UfwRules.Read("ufw allow 51820/udp comment 'kept by amneziageo awg0'\n"));
    }

    [Fact]
    public async Task APortHandedOverStaysInUfwUnderACommentWithoutTheMark()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "ufw allow 51820/udp comment 'amneziageo awg0'\n"
            + "ufw allow 51820/tcp comment 'amneziageo awg0'\n"
            + "ufw allow 8443/tcp comment 'amneziageo panel'\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.KeepAsync([new FirewallPort("udp", 51820, "awg0")], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(["ufw show added", "ufw allow 51820/udp comment kept by amneziageo awg0"], tools.Calls);
    }

    [Fact]
    public async Task APortHandedOverIsNotTakenOutOnceThePanelLetsItGo()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(
            0,
            "ufw allow 8443/tcp comment 'kept by amneziageo panel'\n"
            + "ufw allow 9443/tcp comment 'amneziageo panel'\n",
            string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.ApplyAsync(
            FirewallPlan.Of([], new PanelSettings { Port = 9443, Opened = true }, new SubscriptionSettings()),
            CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(["ufw show added"], tools.Calls);
    }

    [Fact]
    public async Task ARuleOfTheHostIsNotHandedOverAgain()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(0, "ufw allow 51820/udp comment 'amneziawg inbound 6'\n", string.Empty);
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.KeepAsync([new FirewallPort("udp", 51820, "awg0")], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal(["ufw show added"], tools.Calls);
    }

    [Fact]
    public async Task AUfwThatRefusesToKeepAPortLeavesTheReason()
    {
        var tools = new Tools();
        tools.Answers["ufw show added"] = new CommandResult(0, "ufw allow 51820/udp comment 'amneziageo awg0'\n", string.Empty);
        tools.Answers["ufw allow 51820/udp"] = new CommandResult(1, string.Empty, "ERROR: Could not update running firewall");
        var host = new FirewallHost(tools, new Ledger(), "ufw");

        var sync = await host.KeepAsync([new FirewallPort("udp", 51820, "awg0")], CancellationToken.None);

        Assert.False(sync.IsDone);
        Assert.Equal("ERROR: Could not update running firewall", sync.Message);
    }

    [Fact]
    public async Task TheTableOfTheOpenPortsKeepsNothing()
    {
        var tools = new Tools();
        var ledger = new Ledger();
        var host = new FirewallHost(tools, ledger, string.Empty);

        var sync = await host.KeepAsync([new FirewallPort("udp", 51820, "awg0")], CancellationToken.None);

        Assert.True(sync.IsDone);
        Assert.Equal("nft", sync.Engine);
        Assert.Empty(tools.Calls);
        Assert.Empty(ledger.Steps);
    }

    [Fact]
    public void TheUdpPortAnEndpointMovedOffIsDropped()
    {
        var before = FirewallPlan.Of([Endpoint() with { ServicesPort = 8446 }], new PanelSettings(), new SubscriptionSettings());
        var after = FirewallPlan.Of(
            [Endpoint() with { ListenPort = 51821, ServicesPort = 8446 }],
            new PanelSettings(),
            new SubscriptionSettings());

        Assert.Equal([new FirewallPort("udp", 51820, "awg0")], FirewallPlan.Dropped(before, after));
    }

    [Fact]
    public void ARenamedEndpointOnTheSamePortsDropsNothing()
    {
        var before = FirewallPlan.Of([Endpoint()], new PanelSettings(), new SubscriptionSettings());
        var after = FirewallPlan.Of([Endpoint() with { Name = "awg5" }], new PanelSettings(), new SubscriptionSettings());

        Assert.Empty(FirewallPlan.Dropped(before, after));
    }

    [Fact]
    public void APortAnotherOwnerStillHoldsIsNotDropped()
    {
        var subscriptions = new SubscriptionSettings { IsEnabled = true, Separate = true, Port = 8446, Opened = true };
        var before = FirewallPlan.Of([Endpoint() with { ServicesPort = 8446 }], new PanelSettings(), subscriptions);
        var after = FirewallPlan.Of([Endpoint() with { ServicesPort = 8447 }], new PanelSettings(), subscriptions);

        Assert.Empty(FirewallPlan.Dropped(before, after));
        Assert.Equal([new FirewallPort("tcp", 8447, "awg0")], FirewallPlan.Dropped(after, before));
    }

    private static ServerConfig Endpoint() => new()
    {
        Name = "awg0",
        ListenPort = 51820,
        Address = ["10.0.0.1/24"],
        IsEnabled = true,
        Opened = true,
    };
}
