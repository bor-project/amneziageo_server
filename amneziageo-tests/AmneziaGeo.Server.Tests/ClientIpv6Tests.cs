using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientIpv6Tests
{
    [Fact]
    public void CompletionUsesTheExistingIpv4HostOffset()
    {
        var addresses = new[] { "10.8.0.42/32" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], addresses, []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.42/32", "fd11::2a/128"], result.Addresses);
        Assert.Equal(["10.8.0.42/32"], addresses);
    }

    [Fact]
    public void TheOffsetComesFromTheNetworkRatherThanTheLastOctet()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/23", "fd11::1/64"], ["10.8.1.7/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.1.7/32", "fd11::107/128"], result.Addresses);
    }

    [Theory]
    [InlineData("fd11::2a/128")]
    [InlineData("FD11:0:0:0:0:0:0:2A/128")]
    [InlineData("fd11::2a%3/128")]
    public void AnOccupiedPreferredOffsetUsesAFreeAddress(string occupied)
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32"], [occupied]);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.42/32", "fd11::2/128"], result.Addresses);
    }

    [Fact]
    public void AnIpv4AddressOutsideTheInterfaceKeepsItsValue()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["192.0.2.42/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["192.0.2.42/32", "fd11::2/128"], result.Addresses);
    }

    [Fact]
    public void APreferredOffsetOutsideTheIpv6NetworkUsesAFreeAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/23", "fd11::1/120"], ["10.8.1.7/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.1.7/32", "fd11::2/128"], result.Addresses);
    }

    [Fact]
    public void AnExistingIpv4OffsetCanExceedTheAutomaticSearchWindow()
    {
        var result = ClientIpv6.Complete(["10.0.0.1/8", "fd11::1/64"], ["10.1.0.2/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.1.0.2/32", "fd11::1:2/128"], result.Addresses);
    }

    [Fact]
    public void FallbackSearchStopsAtTheExistingPoolLimit()
    {
        var taken = Enumerable.Range(2, (int)ClientPool.MaxSearch - 2).Select(number => $"fd11::{number:x}/128").ToArray();
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["192.0.2.42/32"], taken);
        Assert.Equal("client-ipv6-unavailable", result.Fault?.Code);
    }

    [Fact]
    public void ExistingIpv6TextAndOtherAddressesStayUnchanged()
    {
        var addresses = new[] { "10.8.0.42/32", "FD11:0000:0:0:0:0:0:BEEF/128", "fd22::9/128" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], addresses, []);
        Assert.Null(result.Fault);
        Assert.Same(addresses, result.Addresses);
    }

    [Fact]
    public void AnIpv6AddressInAnotherNetworkDoesNotPreventCompletion()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32", "fd22::9/128"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.42/32", "fd22::9/128", "fd11::2a/128"], result.Addresses);
    }

    [Fact]
    public void EveryMissingIpv6NetworkReceivesAnAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64", "fd12::1/64"], ["10.8.0.42/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.42/32", "fd11::2a/128", "fd12::2a/128"], result.Addresses);
    }

    [Fact]
    public void Ipv4OnlyInterfacesLeaveClientsUntouched()
    {
        var addresses = new[] { "10.8.0.42/32", "fd22::9/128" };
        var result = ClientIpv6.Complete(["10.8.0.1/24"], addresses, []);
        Assert.Null(result.Fault);
        Assert.Same(addresses, result.Addresses);
        var clients = new[] { Member(1, addresses) };
        var plan = ClientIpv6.Plan(Endpoint("10.8.0.1/24"), clients);
        Assert.Null(plan.Fault);
        Assert.Same(clients, plan.Clients);
    }

    [Fact]
    public void CompletionCannotExceedTheAddressLimit()
    {
        var addresses = new[] { "10.8.0.42/32", "192.0.2.1/32", "fd22::9/128", "fd23::9/128" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], addresses, []);
        Assert.Equal("client-ipv6-address-limit", result.Fault?.Code);
        Assert.Same(addresses, result.Addresses);
    }

    [Fact]
    public void TheAddressLimitDoesNotRejectAnAlreadyCompleteClient()
    {
        var addresses = new[] { "10.8.0.42/32", "192.0.2.1/32", "fd11::9/128", "fd23::9/128" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], addresses, []);
        Assert.Null(result.Fault);
        Assert.Same(addresses, result.Addresses);
    }

    [Theory]
    [InlineData("fd11::1/128")]
    [InlineData("fd11::/128")]
    public void ReservedExistingIpv6AddressesAreRejected(string address)
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32", address], []);
        Assert.Equal("client-address-reserved", result.Fault?.Code);
    }

    [Fact]
    public void AnExistingIpv6AddressCannotBelongToAnotherClient()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32", "fd11::9/128"], ["FD11:0:0:0:0:0:0:9/128"]);
        Assert.Equal("client-address-taken", result.Fault?.Code);
    }

    [Fact]
    public void RepeatedExistingIpv6AddressesAreRejected()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32", "fd11::9/128", "FD11:0:0:0:0:0:0:9/128"], []);
        Assert.Equal("client-address-taken", result.Fault?.Code);
    }

    [Fact]
    public void MultipleDistinctExistingIpv6AddressesArePreserved()
    {
        var addresses = new[] { "10.8.0.42/32", "fd11::9/128", "fd11::a/128" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], addresses, []);
        Assert.Null(result.Fault);
        Assert.Same(addresses, result.Addresses);
    }

    [Fact]
    public void AnIpv6RangeCannotStandForAnIndividualClientAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64"], ["10.8.0.42/32", "fd11::9/64"], []);
        Assert.Equal("bad-client-address", result.Fault?.Code);
    }

    [Fact]
    public void AReservedPreferredOffsetUsesTheNextFreeAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::2/64"], ["10.8.0.2/32"], ["fd11::1/128"]);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.2/32", "fd11::3/128"], result.Addresses);
    }

    [Fact]
    public void AnIpv6Slash127CanUseItsZeroAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/127"], ["10.8.0.42/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.42/32", "fd11::/128"], result.Addresses);
    }

    [Fact]
    public void AnIpv6Slash128HasNoAddressBesidesItsEndpoint()
    {
        var addresses = new[] { "10.8.0.42/32" };
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/128"], addresses, []);
        Assert.Equal("client-ipv6-unavailable", result.Fault?.Code);
        Assert.Same(addresses, result.Addresses);
    }

    [Fact]
    public void AnOverlappingRangeNeverAllocatesAnotherEndpointAddress()
    {
        var result = ClientIpv6.Complete(["10.8.0.1/24", "fd11::1/64", "fd11::2/112"], ["10.8.0.2/32"], []);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.2/32", "fd11::3/128"], result.Addresses);
    }

    [Fact]
    public void ADisabledClientStillReservesItsIpv6Address()
    {
        var first = Member(1, ["10.8.0.2/32"]);
        var disabled = Member(2, ["10.8.0.3/32", "fd11::2/128"]) with { IsEnabled = false };
        var result = ClientIpv6.Plan(Endpoint("10.8.0.1/24", "fd11::1/64"), [first, disabled]);
        Assert.Null(result.Fault);
        Assert.Equal(["10.8.0.2/32", "fd11::3/128"], result.Clients[0].Address);
        Assert.Same(disabled, result.Clients[1]);
    }

    [Fact]
    public void AllocationUsesClientIdAndPreservesInputOrder()
    {
        var first = Member(1, ["192.0.2.10/32"]);
        var second = Member(2, ["192.0.2.20/32"]);
        var endpoint = Endpoint("10.8.0.1/24", "fd11::1/64");
        var reverse = ClientIpv6.Plan(endpoint, [second, first]);
        var forward = ClientIpv6.Plan(endpoint, [first, second]);
        Assert.Null(reverse.Fault);
        Assert.Null(forward.Fault);
        Assert.Equal([2L, 1L], reverse.Clients.Select(one => one.Id));
        Assert.Equal("fd11::2/128", reverse.Clients.Single(one => one.Id == 1).Address[1]);
        Assert.Equal("fd11::3/128", reverse.Clients.Single(one => one.Id == 2).Address[1]);
        foreach (var member in forward.Clients)
        {
            Assert.Equal(member.Address, reverse.Clients.Single(one => one.Id == member.Id).Address);
        }
    }

    [Fact]
    public void ARepeatedPlanKeepsAddressesAndAllClientSettings()
    {
        var original = Member(1, ["10.8.0.42/32"]) with
        {
            PrivateKey = "private", PublicKey = "public", PresharedKey = "shared", SubscriptionId = "subscription",
            Note = "saved", TemplateId = 7, DailyLimit = 100, Dns = ["1.1.1.1"], DnsMode = DnsChoice.Own,
            CreatedUtc = DateTimeOffset.UnixEpoch, UpdatedUtc = DateTimeOffset.UnixEpoch.AddDays(1),
        };
        var endpoint = Endpoint("10.8.0.1/24", "fd11::1/64");
        var result = ClientIpv6.Plan(endpoint, [original]);
        Assert.Null(result.Fault);
        Assert.Equal(original with { Address = result.Clients[0].Address }, result.Clients[0]);
        Assert.Equal(["10.8.0.42/32"], original.Address);
        var repeated = ClientIpv6.Plan(endpoint, result.Clients);
        Assert.Null(repeated.Fault);
        Assert.Same(result.Clients, repeated.Clients);
        Assert.Same(result.Clients[0], repeated.Clients[0]);
    }

    [Fact]
    public void AFailedPlanReturnsAllOriginalClients()
    {
        var clients = new[] { Member(1, ["10.8.0.2/32"]), Member(2, ["10.8.0.3/32"]), Member(3, ["10.8.0.4/32"]) };
        var result = ClientIpv6.Plan(Endpoint("10.8.0.1/24", "fd11::1/126"), clients);
        Assert.Equal("client-ipv6-unavailable", result.Fault?.Code);
        Assert.Same(clients, result.Clients);
        Assert.All(clients, one => Assert.Single(one.Address));
    }

    [Fact]
    public void ContradictoryExistingClientAssignmentsAreRejected()
    {
        var clients = new[] { Member(1, ["10.8.0.2/32", "fd11::9/128"]), Member(2, ["10.8.0.3/32", "FD11:0:0:0:0:0:0:9/128"]) };
        var result = ClientIpv6.Plan(Endpoint("10.8.0.1/24", "fd11::1/64"), clients);
        Assert.Equal("client-address-taken", result.Fault?.Code);
        Assert.Same(clients, result.Clients);
    }

    [Fact]
    public void ClientsOfAnotherInterfaceAreNotChangedOrReserved()
    {
        var other = Member(2, ["10.8.0.3/32", "fd11::2/128"]) with { ConfigId = 2 };
        var result = ClientIpv6.Plan(Endpoint("10.8.0.1/24", "fd11::1/64"), [Member(1, ["10.8.0.2/32"]), other]);
        Assert.Null(result.Fault);
        Assert.Equal("fd11::2/128", result.Clients[0].Address[1]);
        Assert.Same(other, result.Clients[1]);
    }

    private static ServerConfig Endpoint(params string[] ranges) => new() { Id = 1, Name = "awg1", Address = ranges };

    private static TunnelClient Member(long id, IReadOnlyList<string> addresses) =>
        new() { Id = id, ConfigId = 1, Name = "member-" + id, Address = addresses };
}
