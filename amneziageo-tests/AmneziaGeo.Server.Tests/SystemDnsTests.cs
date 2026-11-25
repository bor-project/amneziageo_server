using System.Net;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public sealed class SystemDnsTests
{
    [Fact]
    public void DirectDnsExcludesThePanelsExactAddressesAndKeepsTheSystemStub()
    {
        var servers = new[] { "1.1.1.1", "1.1.1.1", "0.0.0.0", "::", "127.0.0.53", "10.8.0.1", "fd00::1", "::ffff:10.8.0.1" };

        Assert.Equal(["1.1.1.1", "127.0.0.53"], SystemDns.Filter(servers.Select(IPAddress.Parse), ["10.8.0.1/24", "fd00::1/64"], 53));
    }

    [Fact]
    public void APanelOnAnotherPortDoesNotExcludeAnExistingDnsService()
    {
        Assert.Equal(["10.8.0.1"], SystemDns.Filter([IPAddress.Parse("10.8.0.1")], ["10.8.0.1/24"], 5353));
    }

    [Fact]
    public void APanelWildcardCannotBecomeItsOwnDnsThroughLoopback()
    {
        var servers = new[] { "127.0.0.53", "::1", "192.0.2.53" }.Select(IPAddress.Parse);

        Assert.Equal(["::1", "192.0.2.53"], SystemDns.Filter(servers, ["0.0.0.0"], 53));
        Assert.Equal(["127.0.0.53", "192.0.2.53"], SystemDns.Filter(servers, ["::"], 53));
    }

    [Fact]
    public void MissingSystemDnsDoesNotSupplyPublicServers()
    {
        Assert.Empty(SystemDns.Filter([], [], 53));
    }
}
