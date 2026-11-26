using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Tests;

public sealed class ConfigIpv6Tests
{
    private const string Four = "10.85.1.1/24";
    private const string Six = "fd85:1::1/64";

    [Fact]
    public void AnAvailablePathAddsAUniqueLocalSubnet()
    {
        var result = ConfigIpv6.Refresh([Four], "", true, []);
        Assert.True(result.HasSpace);
        Assert.Equal(Four, result.Address[0]);
        Assert.Equal(2, result.Address.Count);
        Assert.Equal(result.Address[1], result.AutomaticAddress);
        var range = AwgAllowedIp.Parse(result.AutomaticAddress);
        Assert.True(range.IsSix);
        Assert.Equal(64, range.Cidr);
        Assert.Equal(0xfd, range.Address.GetAddressBytes()[0]);
        Assert.Equal(1, range.Address.GetAddressBytes()[15]);
    }

    [Fact]
    public void RefreshingAnAutomaticSubnetIsIdempotent()
    {
        var before = new[] { Four, Six };
        var result = ConfigIpv6.Refresh(before, Six, true, [], () => throw new InvalidOperationException());
        Assert.Equal(before, result.Address);
        Assert.Equal(Six, result.AutomaticAddress);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManualIpv6IsPreserved(bool available)
    {
        var before = new[] { Four, "2001:db8:85::1/64" };
        var result = ConfigIpv6.Refresh(before, "", available, [], () => throw new InvalidOperationException());
        Assert.Equal(before, result.Address);
        Assert.Empty(result.AutomaticAddress);
    }

    [Fact]
    public void AnUnavailablePathRemovesOnlyTheMarkedAutomaticAddress()
    {
        var result = ConfigIpv6.Refresh([Four, Six, "fd85:99::1/64"], Six, false, []);
        Assert.Equal([Four, "fd85:99::1/64"], result.Address);
        Assert.Empty(result.AutomaticAddress);
    }

    [Fact]
    public void AnUnavailablePathLeavesAnIpv4OnlyDraftUnchanged()
    {
        var result = ConfigIpv6.Refresh([Four], "", false, []);
        Assert.Equal([Four], result.Address);
        Assert.Empty(result.AutomaticAddress);
    }

    [Theory]
    [InlineData("fd85::1/16")]
    [InlineData("fd85:1::9/128")]
    [InlineData("FD85:0001:0000:0000:0000:0000:0000:9999/64")]
    public void AnOverlappingRangeReservesTheWholeCandidateSubnet(string reserved)
    {
        var calls = 0;
        var result = ConfigIpv6.Refresh([Four], "", true, [reserved], () => ++calls == 1 ? Six : "fd86:1::1/64");
        Assert.Equal(2, calls);
        Assert.Equal("fd86:1::1/64", result.AutomaticAddress);
    }

    [Fact]
    public void ACollidingAutomaticSubnetIsReplaced()
    {
        var result = ConfigIpv6.Refresh([Four, Six], Six, true, ["fd85:1::9/128"], () => "fd86:1::1/64");
        Assert.Equal([Four, "fd86:1::1/64"], result.Address);
        Assert.Equal("fd86:1::1/64", result.AutomaticAddress);
    }

    [Fact]
    public void ExhaustionKeepsTheOriginalDraft()
    {
        var calls = 0;
        var original = new[] { Four, Six };
        var result = ConfigIpv6.Refresh(original, Six, true, ["fd00::/8"], () =>
        {
            calls++;
            return "fd86:1::1/64";
        });
        Assert.False(result.HasSpace);
        Assert.Equal(256, calls);
        Assert.Equal(original, result.Address);
        Assert.Equal(Six, result.AutomaticAddress);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.85.1.1/24")]
    [InlineData("fd86:1::1/64")]
    [InlineData("fd85:1::1/128")]
    public void AnInvalidOrStaleMarkerDoesNotDeleteAManualAddress(string marker)
    {
        var result = ConfigIpv6.Refresh([Four, Six], marker, false, []);
        Assert.Equal([Four, Six], result.Address);
        Assert.Empty(result.AutomaticAddress);
    }

    [Fact]
    public void AutomaticMarkersMatchEquivalentAddressNotation()
    {
        var stored = "FD85:0001:0000:0000:0000:0000:0000:0001/64";
        Assert.Equal(stored, ConfigIpv6.Automatic([Four, stored], Six));
        Assert.Equal([Four], ConfigIpv6.Refresh([Four, stored], Six, false, []).Address);
    }

    [Fact]
    public void RetiringAnAutomaticSubnetPreservesOtherClientAddresses()
    {
        var endpoint = ConfigDefaults.Fresh("retire851") with { Id = 1, Address = [Four] };
        var member = ClientDefaults.Fresh(endpoint.Id, "phone") with
        {
            Address = ["10.85.1.7/32", "fd85:1::7/128", "fd85:99::7/128"],
        };
        var result = ClientIpv6.Plan(endpoint, [member], Six);
        Assert.Null(result.Fault);
        Assert.Equal(["10.85.1.7/32", "fd85:99::7/128"], Assert.Single(result.Clients).Address);
        Assert.Equal(member.PrivateKey, result.Clients[0].PrivateKey);
    }
}
