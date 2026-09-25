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
}
