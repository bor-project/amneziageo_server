using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class RangeMergeTests
{
    [Fact]
    public void ARangeInsideAnotherIsFoldedAway()
    {
        Assert.Equal(["10.0.0.0/8"], Merged("10.1.2.3/32", "10.0.0.0/8", "10.1.0.0/16"));
    }

    [Fact]
    public void TwoHalvesFoldIntoTheWhole()
    {
        Assert.Equal(["192.168.0.0/24"], Merged("192.168.0.128/25", "192.168.0.0/25"));
    }

    [Fact]
    public void NeighboursThatDoNotAlignStayApart()
    {
        Assert.Equal(["10.0.0.1/32", "10.0.0.2/32"], Merged("10.0.0.2/32", "10.0.0.1/32"));
    }

    [Fact]
    public void TheWholeSpaceSwallowsEverything()
    {
        Assert.Equal(["0.0.0.0/0", "::/0"], Merged("1.2.3.4/32", "::/0", "2001:db8::/32", "0.0.0.0/0"));
    }

    [Fact]
    public void TheSecondFamilyFoldsToo()
    {
        Assert.Equal(["1.2.3.4/32", "2001:db8::/32"], Merged("2001:db8:8000::/33", "2001:db8::/33", "1.2.3.4/32"));
    }

    private static string[] Merged(params string[] ranges) =>
        [.. RangeMerge.Merge(ranges.Select(AwgAllowedIp.Parse)).Select(range => range.ToString())];
}
