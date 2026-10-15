using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Tests;

public class InterfaceReadsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASecondReadWithinTheRoundTakesTheFirstAndALaterOneReadsAgain()
    {
        var kernel = new Kernel();
        kernel.Hold(new AwgDevice { Name = "awg1" });
        var clock = new Clock(Noon);
        var reads = new InterfaceReads(kernel, clock);

        reads.Find("awg1");
        clock.Pass(TimeSpan.FromSeconds(1));
        var held = reads.Find("awg1");
        var within = kernel.Asked.Count;
        clock.Pass(TimeSpan.FromSeconds(1));
        reads.Find("awg1");

        Assert.Equal("awg1", held?.Name);
        Assert.Equal(1, within);
        Assert.Equal(2, kernel.Asked.Count);
    }

    [Fact]
    public void AChangeOfAnInterfaceHasItReadAnew()
    {
        var kernel = new Kernel();
        kernel.Hold(new AwgDevice { Name = "awg1" });
        var reads = new InterfaceReads(kernel, new Clock(Noon));

        reads.Find("awg1");
        reads.Apply(new AwgUpdate { Name = "awg1" });
        reads.Find("awg1");

        Assert.Equal(2, kernel.Asked.Count);
        Assert.Single(kernel.Updates);
    }

    [Fact]
    public void AnInterfaceTheHostDoesNotCarryIsHeldMissingWithinTheRound()
    {
        var kernel = new Kernel();
        var reads = new InterfaceReads(kernel, new Clock(Noon));

        var first = reads.Find("awg9");
        var second = reads.Find("awg9");

        Assert.Null(first);
        Assert.Null(second);
        Assert.Single(kernel.Asked);
    }
}
