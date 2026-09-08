using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Geo.Format;

namespace AmneziaGeo.Server.Tests;

public class GeoTests
{
    private static readonly byte[] Ip = GeoBuilder.Ip(
        ("RU", ["77.88.8.0/24", "5.255.255.0/24"]),
        ("DE", ["1.2.3.0/24"]));

    private static readonly byte[] Site = GeoBuilder.Site(
        ("YOUTUBE", ["youtube.com", "ytimg.com"]),
        ("CATEGORY-ADS", ["ads.example"]));

    [Fact]
    public void TheCountriesOfADatabaseAreReadBothWays()
    {
        using var stream = new MemoryStream(Ip);

        Assert.Equal(["RU", "DE"], GeoIpDatabase.Countries(Ip));
        Assert.Equal(["RU", "DE"], GeoIpDatabase.Countries(stream));
    }

    [Fact]
    public void TheRangesOfACountryAreReadBothWays()
    {
        using var stream = new MemoryStream(Ip);

        Assert.Equal(["77.88.8.0/24", "5.255.255.0/24"], GeoIpDatabase.Cidrs(Ip, "ru"));
        Assert.Equal(["1.2.3.0/24"], GeoIpDatabase.Cidrs(stream, "de"));
        Assert.Empty(GeoIpDatabase.Cidrs(Ip, "fr"));
    }

    [Fact]
    public void TheCategoriesAndDomainsOfADatabaseAreRead()
    {
        using var stream = new MemoryStream(Site);

        Assert.Equal(["YOUTUBE", "CATEGORY-ADS"], GeoSiteDatabase.Categories(Site));
        Assert.Equal(
            ["youtube.com", "ytimg.com"],
            GeoSiteDatabase.Domains(stream, "youtube").Select(domain => domain.Value));
        Assert.All(GeoSiteDatabase.Domains(Site, "youtube"), domain => Assert.Equal(GeoDomainKind.Domain, domain.Kind));
    }

    [Fact]
    public void ATruncatedDatabaseIsRefusedInsteadOfRead()
    {
        Assert.ThrowsAny<Exception>(() => GeoIpDatabase.Countries(Ip.AsSpan(0, Ip.Length / 2).ToArray()));
    }

    [Fact]
    public void ALaterSourceOverridesAnEarlierOne()
    {
        var files = new MemoryGeoFiles();
        files.Put("one", Ip);
        files.Put("two", GeoBuilder.Ip(("RU", ["10.0.0.0/8"])));

        var index = GeoIndex.Load(
            [
                new GeoSource { Name = "one", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "two", Kind = GeoKind.Ip, Position = 2 },
            ],
            files);

        Assert.Equal(["10.0.0.0/8"], index.Cidrs("ru"));
        Assert.Equal(["DE", "RU"], index.Countries());
    }

    [Fact]
    public void ASourceThatIsOffIsNotRead()
    {
        var files = new MemoryGeoFiles();
        files.Put("one", Ip);

        var index = GeoIndex.Load([new GeoSource { Name = "one", Kind = GeoKind.Ip, IsEnabled = false }], files);

        Assert.Empty(index.Cidrs("ru"));
    }

    [Fact]
    public void AMissingFileLeavesTheOtherSourcesReadable()
    {
        var files = new MemoryGeoFiles();
        files.Put("two", Site);

        var index = GeoIndex.Load(
            [
                new GeoSource { Name = "one", Kind = GeoKind.Site, Position = 1 },
                new GeoSource { Name = "two", Kind = GeoKind.Site, Position = 2 },
            ],
            files);

        Assert.Equal(["ads.example"], index.Domains("category-ads").Select(domain => domain.Value));
    }

    [Fact]
    public void ARuleIsExpandedIntoRangesAndNames()
    {
        var files = new MemoryGeoFiles();
        files.Put("ip", Ip);
        files.Put("site", Site);
        var index = GeoIndex.Load(
            [
                new GeoSource { Name = "ip", Kind = GeoKind.Ip, Position = 1 },
                new GeoSource { Name = "site", Kind = GeoKind.Site, Position = 2 },
            ],
            files);

        var targets = GeoMaterializer.Materialize(
            [
                new GeoRule(GeoRuleKind.GeoIp, "geoip:ru"),
                new GeoRule(GeoRuleKind.GeoSite, "geosite:youtube"),
                new GeoRule(GeoRuleKind.Cidr, "192.168.0.0/16"),
                new GeoRule(GeoRuleKind.Domain, "example.org"),
            ],
            index);

        Assert.Contains("77.88.8.0/24", targets.Cidrs);
        Assert.Contains("192.168.0.0/16", targets.Cidrs);
        Assert.Contains(targets.Domains, domain => domain.Value == "youtube.com");
        Assert.Contains(targets.Domains, domain => domain.Value == "example.org");
        Assert.Contains(targets.Domains, domain => domain.Value == "ru");
        Assert.Contains(targets.Domains, domain => domain.Value == "xn--p1ai");
    }

    [Fact]
    public void ANameIsMatchedByEveryShapeOfEntry()
    {
        var matcher = new DomainMatcher(
        [
            new GeoDomain(GeoDomainKind.Full, "exact.example"),
            new GeoDomain(GeoDomainKind.Domain, "youtube.com"),
            new GeoDomain(GeoDomainKind.Plain, "-ads-"),
            new GeoDomain(GeoDomainKind.Regex, "^cdn[0-9]+[.]net$"),
        ]);

        Assert.True(matcher.Matches("exact.example"));
        Assert.True(matcher.Matches("www.youtube.com."));
        Assert.True(matcher.Matches("some-ads-host.example"));
        Assert.True(matcher.Matches("cdn12.net"));
        Assert.False(matcher.Matches("sub.exact.example"));
        Assert.False(matcher.Matches("youtube.com.evil.example"));
        Assert.Equal(GeoDomainKind.Domain, matcher.Match("music.youtube.com")!.Value.Kind);
    }

    [Fact]
    public void AnExpressionThePlatformRefusesIsDropped()
    {
        var matcher = new DomainMatcher([new GeoDomain(GeoDomainKind.Regex, "([a-z"), new GeoDomain(GeoDomainKind.Full, "kept.example")]);

        Assert.True(matcher.Matches("kept.example"));
        Assert.False(matcher.Matches("anything.example"));
    }

    [Fact]
    public void ACountryOwnsItsOwnDomainsAndAnyOtherTokenOwnsNone()
    {
        Assert.Equal(["de"], CountryDomains.Suffixes("DE"));
        Assert.Contains("su", CountryDomains.Suffixes("ru"));
        Assert.Empty(CountryDomains.Suffixes("youtube"));
    }

    [Theory]
    [InlineData("", "bad-name")]
    [InlineData("Geo", "bad-name")]
    [InlineData("1geo", "bad-name")]
    [InlineData("geo/../etc", "bad-name")]
    public void ANameOutsideTheShapeIsRefused(string name, string code)
    {
        Assert.Equal(code, GeoSourceRules.CheckName(name)!.Code);
    }

    [Fact]
    public void AKindOrAddressOutsideTheShapeIsRefused()
    {
        Assert.Equal("bad-kind", GeoSourceRules.CheckKind("geodns")!.Code);
        Assert.Null(GeoSourceRules.CheckKind("GEOIP"));
        Assert.Equal("bad-url", GeoSourceRules.CheckUrl("file:///etc/passwd")!.Code);
        Assert.Equal("bad-url", GeoSourceRules.CheckUrl("github.com/list.dat")!.Code);
        Assert.Null(GeoSourceRules.CheckUrl("https://example.org/geoip.dat"));
    }
}
