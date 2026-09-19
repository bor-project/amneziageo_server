using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsHandoutTests
{
    private static readonly ServerConfig Endpoint = new()
    {
        Id = 1,
        Name = "awg1",
        Address = ["10.8.0.1/24", "fd00::1/64"],
        Dns = ["1.1.1.1"],
        PrivateKey = "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
        PublicKey = "6isSFA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE=",
    };

    [Fact]
    public void AResolverThatIsOffHandsOutNothing()
    {
        Assert.Empty(DnsHandout.For(Endpoint, DnsDefaults.Settings));
    }

    [Fact]
    public void AResolverOnTheAddressesOfTheConfigurationsHandsOutThem()
    {
        var settings = DnsDefaults.Settings with { IsEnabled = true };

        Assert.Equal(["10.8.0.1", "fd00::1"], DnsHandout.For(Endpoint, settings));
    }

    [Fact]
    public void OnlyTheAddressesTheResolverListensOnAreHandedOut()
    {
        var settings = DnsDefaults.Settings with { IsEnabled = true, Listen = ["10.8.0.1"] };

        Assert.Equal(["10.8.0.1"], DnsHandout.For(Endpoint, settings));
    }

    [Fact]
    public void AResolverElsewhereInTheTunnelHandsOutNothing()
    {
        var settings = DnsDefaults.Settings with { IsEnabled = true, Listen = ["10.9.0.1"] };

        Assert.Empty(DnsHandout.For(Endpoint, settings));
    }

    [Fact]
    public void AResolverOnAnotherPortHandsOutNothing()
    {
        var settings = DnsDefaults.Settings with { IsEnabled = true, Port = 5300 };

        Assert.Empty(DnsHandout.For(Endpoint, settings));
    }
}
