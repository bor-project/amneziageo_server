using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class IpHostNetworkTests
{
    private const string Carried =
        """[{"ifname":"awgbor","addr_info":[{"family":"inet","local":"10.9.0.2","prefixlen":24,"scope":"global"},{"family":"inet","local":"10.9.0.3","prefixlen":24,"scope":"global"},{"family":"inet6","local":"fd00::2","prefixlen":112,"scope":"global"},{"family":"inet6","local":"fe80::1","prefixlen":64,"scope":"link"}]}]""";

    [Fact]
    public async Task ARuleTheHostAlreadyHoldsIsNotAddedAgain()
    {
        var tools = new Tools();
        tools.Answers["ip rule show pref 10000"] = new CommandResult(0, "10000:\tfrom all fwmark 0xa601 lookup main\n", string.Empty);
        var network = new IpHostNetwork(tools);

        await network.RuleAsync(0xA601, 254, 10000, true, CancellationToken.None);

        Assert.False(tools.Called("ip rule add"));
    }

    [Fact]
    public async Task ARuleTheHostDoesNotHoldIsAddedUnderItsPlace()
    {
        var tools = new Tools();
        var network = new IpHostNetwork(tools);

        await network.RuleAsync(0xA602, 42602, 10001, true, CancellationToken.None);

        Assert.True(tools.Called("ip rule add pref 10001 fwmark 42498 lookup 42602"));
    }

    [Fact]
    public async Task ARuleTheHostRefusesToTakeOffLeavesTheCommandStanding()
    {
        var tools = new Tools();
        tools.Answers["ip rule show pref 10001"] = new CommandResult(0, "10001:\tfrom all fwmark 0xa602 lookup main\n", string.Empty);
        tools.Answers["ip rule del"] = new CommandResult(2, string.Empty, "RTNETLINK answers: No such file or directory");
        var network = new IpHostNetwork(tools);

        await network.RuleAsync(0xA602, 42602, 10001, false, CancellationToken.None);

        Assert.True(tools.Called("ip rule del pref 10001 fwmark 42498 lookup 42602"));
    }

    [Fact]
    public async Task ARuleIsTakenOffOnlyWhenTheHostHoldsIt()
    {
        var tools = new Tools();
        tools.Answers["ip rule show pref 10001"] = new CommandResult(0, "10001:\tfrom all fwmark 0xa602 lookup 42602\n", string.Empty);
        var network = new IpHostNetwork(tools);

        await network.RuleAsync(0xA602, 42602, 10001, false, CancellationToken.None);
        var empty = new Tools();
        await new IpHostNetwork(empty).RuleAsync(0xA602, 42602, 10001, false, CancellationToken.None);

        Assert.True(tools.Called("ip rule del pref 10001"));
        Assert.False(empty.Called("ip rule del"));
    }

    [Fact]
    public async Task TheUplinkIsTakenOutOfTheDefaultRoute()
    {
        var tools = new Tools();
        tools.Answers["ip route show default"] =
            new CommandResult(0, "default via 192.168.1.1 dev eth0 proto dhcp src 192.168.1.37 metric 100\n", string.Empty);
        var network = new IpHostNetwork(tools);

        Assert.Equal("eth0", await network.UplinkAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARulesetTheFirewallRefusesIsToldAbout()
    {
        var tools = new Tools();
        tools.Answers["nft"] = new CommandResult(1, string.Empty, "syntax error, unexpected junk\n");
        var network = new IpHostNetwork(tools);

        var refused = await Assert.ThrowsAsync<HostNetworkException>(
            () => network.FirewallAsync("junk", CancellationToken.None));

        Assert.Contains("syntax error", refused.Message, StringComparison.Ordinal);
        Assert.Equal("junk", tools.Input);
    }

    [Fact]
    public async Task AnInterfaceThatDoesNotReadIsGivenItsAddressesAnew()
    {
        var tools = new Tools();
        var network = new IpHostNetwork(tools);

        await network.AddressAsync("awgbor", ["10.9.0.2/32", "fd00::2/128"], CancellationToken.None);

        Assert.Equal(
            [
                "ip -j address show dev awgbor",
                "ip address flush dev awgbor",
                "ip address add 10.9.0.2/32 dev awgbor",
                "ip address add fd00::2/128 dev awgbor",
            ],
            tools.Calls);
    }

    [Fact]
    public async Task TheAddressesAnInterfaceCarriesAreLeftInPlace()
    {
        var tools = new Tools();
        tools.Answers["ip -j address show"] = new CommandResult(0, Carried, string.Empty);
        var network = new IpHostNetwork(tools);

        await network.AddressAsync("awgbor", ["10.9.0.2/24", "10.9.0.3/24", "fd00:0:0::2/112"], CancellationToken.None);

        Assert.Equal(["ip -j address show dev awgbor"], tools.Calls);
    }

    [Fact]
    public async Task OnlyTheAddressesThatDifferAreChanged()
    {
        var tools = new Tools();
        tools.Answers["ip -j address show"] = new CommandResult(0, Carried, string.Empty);
        var network = new IpHostNetwork(tools);

        await network.AddressAsync("awgbor", ["10.9.0.3/24", "fd00::2/112", "10.9.0.4/24"], CancellationToken.None);

        Assert.Equal(
            [
                "ip -j address show dev awgbor",
                "ip address del 10.9.0.2/24 dev awgbor",
                "ip -j address show dev awgbor",
                "ip address add 10.9.0.4/24 dev awgbor",
            ],
            tools.Calls);
    }

    [Fact]
    public async Task ATableOfTheFirewallIsReadAsTheHostGivesIt()
    {
        var tools = new Tools();
        tools.Answers["nft -j list table inet amneziageo_rt"] = new CommandResult(0, "{\"nftables\": []}", string.Empty);
        tools.Answers["nft -j list table inet nope"] = new CommandResult(1, string.Empty, "Error: No such file or directory");
        var network = new IpHostNetwork(tools);

        Assert.Equal("{\"nftables\": []}", await network.ReadFirewallAsync("amneziageo_rt", CancellationToken.None));
        Assert.Empty(await network.ReadFirewallAsync("nope", CancellationToken.None));
    }
}
