using System.Net;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Tests;

public class ListeningTests
{
    [Fact]
    public void APanelWithoutSettingsSitsUnderAPathNoOneGuesses()
    {
        var made = Listening.Draft(new WebOptions { Listen = ["*:8443"] });
        var again = Listening.Draft(new WebOptions { Listen = ["*:8443"] });
        var told = Listening.Draft(new WebOptions { Listen = ["127.0.0.1:8443"], Path = "/" });

        Assert.StartsWith("sub/", made.Path, StringComparison.Ordinal);
        Assert.Matches("^sub/[a-z0-9]{16}$", made.Path);
        Assert.NotEqual(made.Path, again.Path);
        Assert.Equal("/" + made.Path + "/", made.Prefix);
        Assert.Equal("/", told.Prefix);
        Assert.Equal(["127.0.0.1"], told.Listen);
    }

    [Fact]
    public void TheHostAsksAPanelOnEveryAddressAtTheLoopback()
    {
        var plan = Listening.Plan(["*:8443"]);

        Assert.Equal("http://127.0.0.1:8443/api/health", Listening.Health(plan, PanelDefaults.Settings with { Path = "sub/abc" }, secure: false));
    }

    [Fact]
    public void TheHostAsksAPanelUnderACertificateOverTls()
    {
        var plan = Listening.Plan(["*:9443"]);

        Assert.Equal("https://127.0.0.1:9443/api/health", Listening.Health(plan, PanelDefaults.Settings, secure: true));
    }

    [Fact]
    public void TheHostAsksAPanelAtTheLoopbackAmongItsAddresses()
    {
        var plan = Listening.Plan(["192.0.2.10:8443", "[::1]:8443"]);

        Assert.Equal("http://[::1]:8443/api/health", Listening.Health(plan, PanelDefaults.Settings, secure: false));
    }

    [Fact]
    public void TheHostAsksAPanelOnAnOuterAddressUnderItsPath()
    {
        var plan = Listening.Plan(["192.0.2.10:8443"]);

        Assert.Equal("http://192.0.2.10:8443/sub/abc/api/health", Listening.Health(plan, PanelDefaults.Settings with { Path = "sub/abc" }, secure: false));
    }

    [Fact]
    public void AStarBindsEveryAddressOfThePort()
    {
        var plan = Listening.Plan(["*:5080"]);

        Assert.Equal([5080], plan.AnyPorts);
        Assert.Empty(plan.Points);
    }

    [Fact]
    public void AnAddressBindsOnlyItself()
    {
        var plan = Listening.Plan(["127.0.0.1:5080"]);

        Assert.Empty(plan.AnyPorts);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 5080), Assert.Single(plan.Points));
    }

    [Fact]
    public void AnAddressOfTheSixthVersionIsWrittenInBrackets()
    {
        var plan = Listening.Plan(["[::1]:5080"]);

        Assert.Equal(new IPEndPoint(IPAddress.IPv6Loopback, 5080), Assert.Single(plan.Points));
    }

    [Fact]
    public void TheSameEndpointNamedTwiceIsBoundOnce()
    {
        var plan = Listening.Plan(["127.0.0.1:5080", "127.0.0.1:5080", "*:8080", "*:8080"]);

        Assert.Equal([8080], plan.AnyPorts);
        Assert.Single(plan.Points);
    }

    [Fact]
    public void AnInterfaceOfTheHostBindsTheAddressesItCarries()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var plan = Listening.Plan(["lo:5080"]);

        Assert.Empty(plan.Missing);
        Assert.Contains(new IPEndPoint(IPAddress.Loopback, 5080), plan.Points);
    }

    [Fact]
    public void AnInterfaceTheHostDoesNotCarryIsWrittenDown()
    {
        var plan = Listening.Plan(["nosuchlink0:5080"]);

        Assert.True(plan.IsEmpty);
        Assert.Equal("nosuchlink0", Assert.Single(plan.Missing));
    }

    [Theory]
    [InlineData("5080")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("127.0.0.1:70000")]
    public void AnEntryWithoutAPortIsRefused(string entry)
    {
        Assert.Throws<InvalidOperationException>(() => Listening.Plan([entry]));
    }
}
