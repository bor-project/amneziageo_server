using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class IpHostNetworkTests
{
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
    public async Task AnAddressIsReplacedNotAdded()
    {
        var tools = new Tools();
        var network = new IpHostNetwork(tools);

        await network.AddressAsync("awgbor", ["10.9.0.2/32", "fd00::2/128"], CancellationToken.None);

        Assert.Equal(
            ["ip address flush dev awgbor", "ip address add 10.9.0.2/32 dev awgbor", "ip address add fd00::2/128 dev awgbor"],
            tools.Calls);
    }
}
