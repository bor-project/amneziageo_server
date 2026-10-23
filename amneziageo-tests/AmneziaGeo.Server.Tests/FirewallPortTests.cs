using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public sealed class FirewallPortTests
{
    private const string Active = """
        Status: active
        Logging: on (low)
        Default: deny (incoming), allow (outgoing), deny (routed)
        New profiles: skip

        To                         Action      From
        --                         ------      ----
        22/tcp                     ALLOW IN    Anywhere
        8443/tcp                   ALLOW IN    Anywhere                   # amneziageo panel
        51820/udp                  ALLOW IN    Anywhere
        9000:9100/tcp              ALLOW IN    Anywhere
        80,443/tcp                 ALLOW IN    Anywhere
        7000                       DENY IN     Anywhere
        7000/tcp                   ALLOW IN    Anywhere
        OpenSSH                    ALLOW IN    Anywhere
        6000/tcp on eth0           ALLOW IN    Anywhere
        10.0.0.1 4000/tcp          LIMIT IN    Anywhere
        22/tcp (v6)                ALLOW IN    Anywhere (v6)
        5000/tcp (v6)              ALLOW IN    Anywhere (v6)
        Anywhere on awg0           ALLOW FWD   Anywhere on eth0
        3000/tcp                   ALLOW OUT   Anywhere
        """;

    private const string Input = """
        table ip filter {
            chain INPUT {
                type filter hook input priority filter; policy drop;
                counter packets 10 bytes 600 jump ufw-before-logging-input
            }
        }
        """;

    private const string Rules = """
        table ip filter {
            chain ufw-user-input {
                tcp dport 22 counter packets 0 bytes 0 accept
                tcp dport 8443 counter packets 3 bytes 180 accept
                udp dport 51820 counter packets 0 bytes 0 accept
                tcp dport 9000-9100 counter packets 0 bytes 0 accept
                tcp dport { 80, 443 } counter packets 0 bytes 0 accept
                tcp dport 7000 counter packets 0 bytes 0 drop
                tcp dport 7000 counter packets 0 bytes 0 accept
                ip saddr 203.0.113.5 tcp dport 6000 counter packets 0 bytes 0 accept
                tcp dport 4000 counter packets 0 bytes 0 jump ufw-user-limit-accept
            }
        }
        """;

    private const string Unread = """
        table ip filter {
            chain ufw-user-input {
                meta l4proto 6 xt match "tcp" xt match "comment" counter packets 5113 bytes 297269 accept
                meta l4proto 17 xt match "multiport" counter packets 141 bytes 46285 accept
                meta l4proto 6 xt match "tcp" counter packets 185090 bytes 10906097 accept
                meta l4proto 17 xt match "udp" counter packets 166 bytes 156198 accept
                meta l4proto 17 xt match "udp" counter packets 258 bytes 124263 accept
                meta l4proto 6 xt match "tcp" counter packets 66058 bytes 3895472 accept
            }
        }
        """;

    [Theory]
    [InlineData("tcp", 8443, PortState.Open)]
    [InlineData("tcp", 22, PortState.Open)]
    [InlineData("tcp", 9050, PortState.Open)]
    [InlineData("tcp", 443, PortState.Open)]
    [InlineData("tcp", 6000, PortState.Open)]
    [InlineData("tcp", 4000, PortState.Open)]
    [InlineData("udp", 51820, PortState.Open)]
    [InlineData("tcp", 9443, PortState.Closed)]
    [InlineData("tcp", 51820, PortState.Closed)]
    [InlineData("tcp", 7000, PortState.Closed)]
    [InlineData("tcp", 5000, PortState.Closed)]
    [InlineData("tcp", 3000, PortState.Closed)]
    public void UfwTellsAPortByTheFirstRuleThatNamesItAndByItsPolicyOtherwise(string protocol, int port, string state)
    {
        Assert.Equal(state, PortState.OfUfw(Active, protocol, port));
    }

    [Fact]
    public void AnInactiveUfwOrAPolicyThatLetsInOpenEveryPort()
    {
        var letting = "Status: active\nDefault: allow (incoming), allow (outgoing), disabled (routed)\n\n"
            + "To                         Action      From\n--                         ------      ----\n";

        Assert.Equal(PortState.Open, PortState.OfUfw("Status: inactive\n", "tcp", 9443));
        Assert.Equal(PortState.Open, PortState.OfUfw(letting, "tcp", 9443));
        Assert.Equal(PortState.Unknown, PortState.OfUfw("ERROR: You need to be root to run this script\n", "tcp", 9443));
    }

    [Fact]
    public void ARuleForEveryPortFromOneAddressLetsThePortIn()
    {
        var status = """
            Status: active
            Default: deny (incoming), allow (outgoing), deny (routed)

            To                         Action      From
            --                         ------      ----
            Anywhere                   ALLOW IN    203.0.113.5
            """;

        Assert.Equal(PortState.Open, PortState.OfUfw(status, "tcp", 9443));
    }

    [Theory]
    [InlineData("tcp", 8443, PortState.Open)]
    [InlineData("tcp", 9050, PortState.Open)]
    [InlineData("tcp", 443, PortState.Open)]
    [InlineData("tcp", 6000, PortState.Open)]
    [InlineData("tcp", 4000, PortState.Open)]
    [InlineData("udp", 51820, PortState.Open)]
    [InlineData("tcp", 9443, PortState.Closed)]
    [InlineData("tcp", 51820, PortState.Closed)]
    [InlineData("tcp", 7000, PortState.Closed)]
    public void TheChainsOfUfwInNftablesTellAPortTheSameWay(string protocol, int port, string state)
    {
        Assert.Equal(state, PortState.OfChains(Input, Rules, protocol, port));
    }

    [Fact]
    public void AnInputChainThatAcceptsLetsEveryPortIn()
    {
        var accepting = Input.Replace("policy drop;", "policy accept;", StringComparison.Ordinal);

        Assert.Equal(PortState.Open, PortState.OfChains(accepting, Rules, "tcp", 9443));
    }

    [Theory]
    [InlineData("udp", 51820)]
    [InlineData("udp", 443)]
    [InlineData("tcp", 443)]
    [InlineData("tcp", 8443)]
    [InlineData("tcp", 9443)]
    public void ARuleWhosePortNftablesDoesNotShowLeavesThePortUnknown(string protocol, int port)
    {
        Assert.Equal(PortState.Unknown, PortState.OfChains(Input, Unread, protocol, port));
    }

    [Fact]
    public void ARuleThatNamesThePortAheadOfOneWithoutAPortStillDecides()
    {
        var rules = Unread.Replace(
            "chain ufw-user-input {",
            "chain ufw-user-input {\n        tcp dport 8443 counter packets 3 bytes 180 accept\n        tcp dport 7000 counter packets 0 bytes 0 drop",
            StringComparison.Ordinal);

        Assert.Equal(PortState.Open, PortState.OfChains(Input, rules, "tcp", 8443));
        Assert.Equal(PortState.Closed, PortState.OfChains(Input, rules, "tcp", 7000));
        Assert.Equal(PortState.Unknown, PortState.OfChains(Input, rules, "tcp", 443));
    }

    [Fact]
    public async Task TheHostAsksUfwWhereItHasUfwAndTheChainsOfNftablesElsewhere()
    {
        var withUfw = new Tools();
        withUfw.Answers["ufw status verbose"] = new CommandResult(0, Active, string.Empty);
        var bare = new Tools();
        bare.Answers["nft list chain ip filter INPUT"] = new CommandResult(0, Input, string.Empty);
        bare.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(0, Rules, string.Empty);
        var none = new Tools();
        none.Answers["nft list chain"] = new CommandResult(1, string.Empty, "Error: No such file or directory");

        Assert.Equal(PortState.Closed, await new FirewallHost(withUfw, new Ledger(), "ufw").StateAsync("tcp", 9443, CancellationToken.None));
        Assert.Equal(PortState.Open, await new FirewallHost(bare, new Ledger(), string.Empty).StateAsync("tcp", 8443, CancellationToken.None));
        Assert.Equal(PortState.Closed, await new FirewallHost(bare, new Ledger(), string.Empty).StateAsync("tcp", 9443, CancellationToken.None));
        Assert.Equal(PortState.Unknown, await new FirewallHost(none, new Ledger(), string.Empty).StateAsync("tcp", 8443, CancellationToken.None));
        Assert.True(withUfw.Called("ufw status verbose"));
    }

    [Fact]
    public async Task AHostWhoseRulesNftablesDoesNotShowTellsNothingOfItsPorts()
    {
        var tools = new Tools();
        tools.Answers["nft list chain ip filter INPUT"] = new CommandResult(0, Input, string.Empty);
        tools.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(0, Unread, "# Warning: XT match tcp not found\n");

        var states = await new FirewallHost(tools, new Ledger(), string.Empty).StatesAsync(
            [new FirewallPort("udp", 443, string.Empty), new FirewallPort("tcp", 443, string.Empty)],
            CancellationToken.None);

        Assert.Equal([PortState.Unknown, PortState.Unknown], states);
    }

    private const string Tunnels = """
        Status: active
        Logging: on (low)
        Default: deny (incoming), allow (outgoing), allow (routed)
        New profiles: skip

        To                         Action      From
        --                         ------      ----
        22/tcp                     ALLOW IN    Anywhere
        53 on awg0                 ALLOW IN    Anywhere                   # amneziageo resolver awg0
        53 on awg2                 ALLOW IN    10.0.2.0/24
        Anywhere on awg3           ALLOW IN    Anywhere
        53/tcp on awg4             ALLOW IN    Anywhere
        53 on awg6                 ALLOW IN    10.0.6.5
        53 (v6) on awg0            ALLOW IN    Anywhere (v6)
        """;

    private const string TunnelRules = """
        table ip filter {
            chain ufw-user-input {
                meta l4proto tcp tcp dport 22 counter packets 0 bytes 0 accept
                iifname "awg0" meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg0" meta l4proto tcp tcp dport 53 counter packets 0 bytes 0 accept
                iifname "wg*" meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg2" ip saddr 10.0.2.0/24 meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg3" counter packets 0 bytes 0 accept
                iifname "awg4" meta l4proto tcp counter packets 0 bytes 0 accept
                iifname "awg5" meta l4proto udp udp dport 53 counter packets 0 bytes 0 drop
                iifname { "awg6", "awg7" } meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg8" ip saddr { 10.0.8.0/24, 10.0.9.0/24 } meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg9" ip saddr != 10.0.9.0/24 meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
            }
        }
        """;

    private const string Before = """
        table ip filter {
            chain ufw-before-input {
                iifname "lo" counter packets 0 bytes 0 accept
                ct state related,established counter packets 0 bytes 0 accept
                ct state invalid counter packets 0 bytes 0 drop
                meta l4proto udp udp sport 67 udp dport 68 counter packets 0 bytes 0 accept
                counter packets 0 bytes 0 jump ufw-user-input
            }
        }
        """;

    private const string Rules6 = """
        table ip6 filter {
            chain ufw6-user-input {
                iifname "awg0" meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg2" ip6 saddr fd00:2::/64 meta l4proto udp udp dport 53 counter packets 0 bytes 0 accept
                iifname "awg3" counter packets 0 bytes 0 accept
            }
        }
        """;

    [Theory]
    [InlineData("awg0", PortState.Open)]
    [InlineData("awg1", PortState.Closed)]
    [InlineData("awg2", PortState.Closed)]
    [InlineData("awg3", PortState.Open)]
    [InlineData("awg4", PortState.Closed)]
    public void UfwTellsAPortOfOneInterfaceByTheRulesForItOrForEveryOne(string inbound, string state)
    {
        Assert.Equal(state, PortState.OfUfw(Tunnels, "udp", 53, inbound));
    }

    [Theory]
    [InlineData("awg0", PortState.Open)]
    [InlineData("wg7", PortState.Open)]
    [InlineData("awg1", PortState.Closed)]
    [InlineData("awg2", PortState.Closed)]
    [InlineData("awg3", PortState.Open)]
    [InlineData("awg4", PortState.Closed)]
    [InlineData("awg5", PortState.Closed)]
    [InlineData("awg7", PortState.Open)]
    public void TheChainsOfUfwTellAPortOfOneInterfaceTheSameWay(string inbound, string state)
    {
        Assert.Equal(state, PortState.OfChains(Input, Before + TunnelRules, "udp", 53, inbound));
    }

    [Theory]
    [InlineData("awg2", "10.0.2.1/24", PortState.Open)]
    [InlineData("awg2", "10.0.2.1/25", PortState.Open)]
    [InlineData("awg2", "10.0.2.1/24,fd00:2::1/64", PortState.Open)]
    [InlineData("awg2", "10.0.0.1/16", PortState.Closed)]
    [InlineData("awg2", "10.0.3.1/24", PortState.Closed)]
    [InlineData("awg2", "10.0.2.1/24,10.0.3.1/24", PortState.Closed)]
    [InlineData("awg2", "fd00:2::1/64", PortState.Closed)]
    [InlineData("awg2", "", PortState.Closed)]
    [InlineData("awg6", "10.0.6.5/32", PortState.Open)]
    [InlineData("awg6", "10.0.6.1/24", PortState.Closed)]
    [InlineData("awg0", "10.0.0.1/24", PortState.Open)]
    [InlineData("awg1", "10.0.1.1/24", PortState.Closed)]
    public void ARuleOfUfwFromTheSubnetOfAnInterfaceLetsItsClientsIn(string inbound, string addresses, string state)
    {
        Assert.Equal(state, PortState.OfUfw(Tunnels, "udp", 53, inbound, Split(addresses)));
    }

    [Theory]
    [InlineData("awg2", "10.0.2.1/24", PortState.Open)]
    [InlineData("awg2", "10.0.2.1/25", PortState.Open)]
    [InlineData("awg2", "10.0.0.1/16", PortState.Closed)]
    [InlineData("awg2", "10.0.3.1/24", PortState.Closed)]
    [InlineData("awg2", "", PortState.Closed)]
    [InlineData("awg8", "10.0.8.1/24", PortState.Open)]
    [InlineData("awg8", "10.0.9.1/24", PortState.Open)]
    [InlineData("awg8", "10.0.8.1/23", PortState.Closed)]
    [InlineData("awg9", "10.0.9.1/24", PortState.Closed)]
    [InlineData("awg0", "10.0.0.1/24", PortState.Open)]
    public void ARuleOfTheChainsFromTheSubnetOfAnInterfaceLetsItsClientsIn(string inbound, string addresses, string state)
    {
        Assert.Equal(state, PortState.OfChains(Input, Before + TunnelRules, "udp", 53, inbound, Split(addresses)));
    }

    [Fact]
    public void ARuleOfTheSixChainsFromTheSubnetOfAnInterfaceLetsItsClientsIn()
    {
        Assert.Equal(PortState.Open, PortState.OfChains(Input, Rules6, "udp", 53, "awg2", ["10.0.2.1/24", "fd00:2::1/64"]));
        Assert.Equal(PortState.Closed, PortState.OfChains(Input, Rules6, "udp", 53, "awg2", ["10.0.2.1/24", "fd00:3::1/64"]));
        Assert.Equal(PortState.Closed, PortState.OfChains(Input, Rules6, "udp", 53, "awg2", ["10.0.2.1/24"]));
    }

    [Fact]
    public void AnInterfaceLetsThePortInWhereTheFirewallLetsEverythingIn()
    {
        var accepting = Input.Replace("policy drop;", "policy accept;", StringComparison.Ordinal);

        Assert.Equal(PortState.Open, PortState.OfChains(accepting, Before, "udp", 53, "awg1"));
        Assert.Equal(PortState.Open, PortState.OfUfw("Status: inactive\n", "udp", 53, "awg1"));
    }

    [Fact]
    public async Task TheHostNamesTheInterfacesItsFirewallKeepsFromAPortOverEitherFamily()
    {
        var chains = new Tools();
        chains.Answers["nft list chain ip filter INPUT"] = new CommandResult(0, Input, string.Empty);
        chains.Answers["nft list chain ip filter ufw-before-input"] = new CommandResult(0, Before, string.Empty);
        chains.Answers["nft list chain ip filter ufw-user-input"] = new CommandResult(0, TunnelRules, string.Empty);
        chains.Answers["nft list chain ip6 filter INPUT"] = new CommandResult(0, Input, string.Empty);
        chains.Answers["nft list chain ip6 filter ufw6-before-input"] = new CommandResult(0, Before, string.Empty);
        chains.Answers["nft list chain ip6 filter ufw6-user-input"] = new CommandResult(0, Rules6, string.Empty);
        var bare = new Tools();
        bare.Answers["nft list chain"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var withUfw = new Tools();
        withUfw.Answers["ufw status verbose"] = new CommandResult(0, Tunnels, string.Empty);

        Assert.Equal(
            ["awg1", "awg8", "wg7"],
            await new FirewallHost(chains, new Ledger(), string.Empty).KeptOutAsync(
                [
                    Door("awg0", "10.0.0.1/24", "fd00::1/64"),
                    Door("awg1", "10.0.1.1/24"),
                    Door("awg2", "10.0.2.1/24"),
                    Door("awg8", "10.0.8.1/24", "fd00:8::1/64"),
                    Door("wg7", "10.0.7.1/24", "fd00:7::1/64"),
                    Door("awg3", "10.0.3.1/24", "fd00:3::1/64"),
                ],
                53,
                CancellationToken.None));
        Assert.Empty(await new FirewallHost(bare, new Ledger(), string.Empty).KeptOutAsync([Door("awg0", "10.0.0.1/24")], 53, CancellationToken.None));
        Assert.Equal(
            ["awg1", "awg4", "awg6"],
            await new FirewallHost(withUfw, new Ledger(), "ufw").KeptOutAsync(
                [
                    Door("awg0", "10.0.0.1/24"),
                    Door("awg1", "10.0.1.1/24"),
                    Door("awg2", "10.0.2.1/24"),
                    Door("awg3", "10.0.3.1/24"),
                    Door("awg4", "10.0.4.1/24"),
                    Door("awg6", "10.0.6.1/24"),
                ],
                53,
                CancellationToken.None));
        Assert.Empty(await new FirewallHost(chains, new Ledger(), string.Empty).KeptOutAsync([], 53, CancellationToken.None));
    }

    private static Doorway Door(string name, params string[] addresses) => new(name, addresses);

    private static string[] Split(string addresses) => addresses.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
