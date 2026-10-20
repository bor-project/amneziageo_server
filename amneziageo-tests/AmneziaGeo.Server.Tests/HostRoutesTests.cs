using System.Net;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class HostRoutesTests
{
    private const string Four = """
        Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask		MTU	Window	IRTT
        eth0	00000000	014DA8C0	0003	0	0	100	00000000	0	0	0
        eth0	004DA8C0	00000000	0001	0	0	100	00FFFFFF	0	0	0
        eth1	00000A0A	014DA8C0	0003	0	0	0	0000FFFF	0	0	0
        eth1	00000B0A	00000000	0201	0	0	0	0000FFFF	0	0	0
        eth1	00000C0A	00000000	0000	0	0	0	0000FFFF	0	0	0
        """;

    private const string Six = """
        fd120000000000000000000000000000 30 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000001 00000000 00000001     eth0
        fd340000000000000000000000000000 30 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000001 00000000 00000201       lo
        00000000000000000000000000000000 00 00000000000000000000000000000000 00 fd120000000000000000000000000001 00000400 00000001 00000000 00000003     eth0
        """;

    [Fact]
    public void TheRoutesOfIPv4ThatAreUpAreReadWithTheirPrefixes()
    {
        var routes = HostRoutes.Four(Four);

        Assert.Equal(["0.0.0.0/0", "192.168.77.0/24", "10.10.0.0/16", "10.11.0.0/16"], routes.Select(route => route.Range));
        Assert.Equal([false, false, false, true], routes.Select(route => route.IsReject));
    }

    [Fact]
    public void TheRoutesOfIPv6ThatAreUpAreReadWithTheirPrefixes()
    {
        var routes = HostRoutes.Six(Six);

        Assert.Equal(["fd12::/48", "fd34::/48", "::/0"], routes.Select(route => route.Range));
        Assert.Equal([false, true, false], routes.Select(route => route.IsReject));
    }

    [Theory]
    [InlineData("192.168.77.53", true)]
    [InlineData("10.10.3.4", true)]
    [InlineData("10.11.0.1", false)]
    [InlineData("10.12.0.1", false)]
    [InlineData("172.16.5.5", false)]
    [InlineData("fd12::53", true)]
    [InlineData("fd34::53", false)]
    [InlineData("fd56::53", false)]
    public void AnAddressIsBehindTheHostByARouteOfItsOwnNotByTheDefaultOne(string address, bool behind) =>
        Assert.Equal(behind, HostRoutes.Behind(IPAddress.Parse(address), [.. HostRoutes.Four(Four), .. HostRoutes.Six(Six)]));

    [Fact]
    public void NoRoutesPutNothingBehindTheHost()
    {
        Assert.Empty(HostRoutes.Four(string.Empty));
        Assert.Empty(HostRoutes.Six(string.Empty));
        Assert.False(HostRoutes.Behind(IPAddress.Parse("192.168.77.53"), []));
    }
}
