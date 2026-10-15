using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsPaceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheResolverRestsUntilTheSoonestWork()
    {
        var rest = DnsPace.Rest(Noon, Noon.AddSeconds(40), Noon.AddSeconds(3), Noon.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(3), rest);
    }

    [Fact]
    public void WorkThatIsDueAlreadyLeavesTheShortestRest()
    {
        var rest = DnsPace.Rest(Noon, Noon.AddSeconds(-10), Noon.AddSeconds(5));

        Assert.Equal(DnsPace.Least, rest);
    }

    [Fact]
    public void WithoutWorkSoonTheResolverRestsAMinute()
    {
        var rest = DnsPace.Rest(Noon, DateTimeOffset.MaxValue, Noon.AddHours(1));

        Assert.Equal(DnsPace.Most, rest);
    }

    [Fact]
    public void AKeptPlanIsTold()
    {
        var plans = new RoutePlans();
        var told = 0;
        plans.Kept += () => told++;

        var plan = new RoutePlan([], []);
        plans.Keep(plan);

        Assert.Equal(1, told);
        Assert.Same(plan, plans.Held);
    }
}
