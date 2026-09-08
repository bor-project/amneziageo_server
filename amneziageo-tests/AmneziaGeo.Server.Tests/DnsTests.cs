using System.Net;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsTests
{
    [Fact]
    public void AQuestionIsReadWithTheNameItAsksAbout()
    {
        var read = DnsMessage.Read(DnsBuilder.Question(42, "www.example.com"));

        Assert.NotNull(read);
        Assert.Equal(42, read.Id);
        Assert.False(read.IsResponse);
        Assert.Equal("www.example.com", read.Question);
        Assert.Equal(DnsRecordType.A, read.Type);
        Assert.Empty(read.Answers);
    }

    [Fact]
    public void AnAnswerGivesTheAddressesItCarries()
    {
        var packet = DnsBuilder.Answer(
            7,
            "example.com",
            DnsRecordType.A,
            new Told("example.com", DnsRecordType.A, 300, "1.2.3.4"),
            new Told("example.com", DnsRecordType.A, 120, "5.6.7.8"));

        var read = DnsMessage.Read(packet);

        Assert.NotNull(read);
        Assert.True(read.IsResponse);
        Assert.Equal(0, read.Code);
        Assert.Equal(["1.2.3.4", "5.6.7.8"], read.Addresses.Select(one => one.ToString()));
        Assert.Equal(120u, read.Lifetime);
    }

    [Fact]
    public void ANameWrittenAsAPointerIsFollowed()
    {
        var packet = DnsBuilder.Answer(
            1,
            "cdn.example.com",
            DnsRecordType.A,
            new Told("cdn.example.com", DnsRecordType.A, 60, "9.9.9.9"));

        var read = DnsMessage.Read(packet);

        Assert.NotNull(read);
        Assert.Equal("cdn.example.com", Assert.Single(read.Answers).Name);
    }

    [Fact]
    public void AnAnswerThroughAnAliasKeepsBothNames()
    {
        var packet = DnsBuilder.Answer(
            2,
            "www.shop.com",
            DnsRecordType.A,
            new Told("www.shop.com", DnsRecordType.Cname, 300, "edge.cdn.net"),
            new Told("edge.cdn.net", DnsRecordType.A, 300, "203.0.113.7"));

        var read = DnsMessage.Read(packet);

        Assert.NotNull(read);
        Assert.Equal(["www.shop.com", "edge.cdn.net"], read.Answers.Select(record => record.Name));
        Assert.Equal("203.0.113.7", Assert.Single(read.Addresses).ToString());
    }

    [Fact]
    public void AnAnswerCarryingAnIpv6AddressIsRead()
    {
        var packet = DnsBuilder.Answer(
            3,
            "six.example.com",
            DnsRecordType.Aaaa,
            new Told("six.example.com", DnsRecordType.Aaaa, 60, "2a02:6b8::1"));

        var read = DnsMessage.Read(packet);

        Assert.NotNull(read);
        Assert.Equal("2a02:6b8::1", Assert.Single(read.Addresses).ToString());
    }

    [Fact]
    public void BytesShorterThanAHeaderAreNotAMessage() => Assert.Null(DnsMessage.Read(new byte[6]));

    [Fact]
    public void ANameThatPointsAtItselfIsRefused() => Assert.Null(DnsMessage.Read(DnsBuilder.Looping()));

    [Fact]
    public void ARefusalAnswersTheQuestionWithTheFailure()
    {
        var refusal = DnsMessage.Refusal(DnsBuilder.Question(11, "example.com"), DnsMessage.ServerFailure);

        var read = DnsMessage.Read(refusal);

        Assert.NotNull(read);
        Assert.Equal(11, read.Id);
        Assert.True(read.IsResponse);
        Assert.Equal(DnsMessage.ServerFailure, read.Code);
        Assert.Empty(read.Answers);
    }

    [Theory]
    [InlineData("1.1.1.1", "1.1.1.1", 53)]
    [InlineData("8.8.8.8:5300", "8.8.8.8", 5300)]
    [InlineData("2606:4700:4700::1111", "2606:4700:4700::1111", 53)]
    [InlineData("[2606:4700:4700::1111]:5300", "2606:4700:4700::1111", 5300)]
    public void AnUpstreamIsReadWithItsPort(string text, string address, int port)
    {
        Assert.True(DnsRules.Upstream(text, out var point));
        Assert.Equal(address, point.Address.ToString());
        Assert.Equal(port, point.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("what.a.name")]
    [InlineData("1.1.1.1:0")]
    [InlineData("1.1.1.1:99999")]
    [InlineData("[2606::1")]
    public void AnUpstreamThatMeansNothingIsRefused(string text) => Assert.False(DnsRules.Upstream(text, out _));

    [Fact]
    public void SettingsThatFollowTheRulesPass() => Assert.Null(DnsRules.Check(DnsDefaults.Settings));

    [Theory]
    [InlineData(0, "bad-port")]
    [InlineData(70000, "bad-port")]
    public void APortOutsideTheRangeIsRefused(int port, string code) =>
        Assert.Equal(code, DnsRules.Check(DnsDefaults.Settings with { Port = port })?.Code);

    [Fact]
    public void SettingsWithoutAnUpstreamAreRefused() =>
        Assert.Equal("no-upstream", DnsRules.Check(DnsDefaults.Settings with { Upstreams = [] })?.Code);

    [Fact]
    public void AnUpstreamThatIsNotAnAddressIsRefused() =>
        Assert.Equal("bad-upstream", DnsRules.Check(DnsDefaults.Settings with { Upstreams = ["resolver"] })?.Code);

    [Fact]
    public void AnAddressToListenOnThatIsNotOneIsRefused() =>
        Assert.Equal("bad-listen", DnsRules.Check(DnsDefaults.Settings with { Listen = ["awg1"] })?.Code);

    [Fact]
    public void ALifetimeOutsideTheRangeIsRefused() =>
        Assert.Equal("bad-lifetime", DnsRules.Check(DnsDefaults.Settings with { NameMinutes = 0 })?.Code);

    [Fact]
    public void AShortestLongerThanTheLongestIsRefused() =>
        Assert.Equal("bad-ttl", DnsRules.Check(DnsDefaults.Settings with { MinTtl = 600, MaxTtl = 60 })?.Code);
}
