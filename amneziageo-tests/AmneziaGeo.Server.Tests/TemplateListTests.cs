using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class TemplateListTests
{
    [Theory]
    [InlineData("geoip:RU", "geoip:ru")]
    [InlineData("GeoSite:YouTube", "geosite:youtube")]
    [InlineData("domain:Example.COM", "example.com")]
    [InlineData("https://www.youtube.com/watch?v=1", "www.youtube.com")]
    [InlineData("cidr:10.1.2.3/8", "10.0.0.0/8")]
    [InlineData("1.2.3.4/32", "1.2.3.4")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("2001:db8::1/32", "2001:db8::/32")]
    public void AnEntryIsKeptInOneForm(string written, string kept)
    {
        Assert.Equal(kept, TemplateList.Entry(written));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a thing")]
    [InlineData("geoip:")]
    [InlineData("youtube")]
    public void TextThatIsNoEntryReadsAsNone(string written)
    {
        Assert.Null(TemplateList.Entry(written));
    }

    [Fact]
    public void AListWithAnEntryThatIsNoneIsRefused()
    {
        Assert.Equal("bad-entry", TemplateList.Check(["geoip:ru", "not a thing"])!.Code);
    }

    [Fact]
    public void AListLongerThanTheCeilingIsRefused()
    {
        var entries = Enumerable.Range(0, TemplateList.MaxEntries + 1).Select(n => $"10.0.{n / 256}.{n % 256}").ToArray();

        Assert.Equal("too-many-entries", TemplateList.Check(entries)!.Code);
    }

    [Fact]
    public void AListOfEveryKindHolds()
    {
        Assert.Null(TemplateList.Check(["geoip:ru", "geosite:youtube", "10.0.0.0/8", "1.2.3.4", "example.com"]));
    }
}
