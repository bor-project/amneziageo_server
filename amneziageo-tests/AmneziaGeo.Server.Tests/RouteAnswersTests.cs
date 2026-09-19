using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteAnswersTests
{
    [Fact]
    public void ARuleThatIsAddedHoldsItsTrafficUnlessTheRequestSaysOtherwise()
    {
        Assert.True(RouteAnswers.Draft(Request(null)).HoldsWhenDown);
        Assert.False(RouteAnswers.Draft(Request(false)).HoldsWhenDown);
    }

    [Fact]
    public void ARequestThatStaysSilentKeepsTheHoldTheRuleCarries()
    {
        Assert.False(RouteAnswers.Draft(Request(null), Held(false)).HoldsWhenDown);
        Assert.True(RouteAnswers.Draft(Request(null), Held(true)).HoldsWhenDown);
    }

    [Fact]
    public void ARuleThatIsAddedStaysOffUnlessTheRequestSaysOtherwise()
    {
        Assert.False(RouteAnswers.Draft(Request(null) with { IsEnabled = null }).IsEnabled);
        Assert.True(RouteAnswers.Draft(Request(null) with { IsEnabled = true }).IsEnabled);
        Assert.False(RouteAnswers.Draft(Request(null) with { IsEnabled = false }).IsEnabled);
    }

    [Fact]
    public void ARequestThatStaysSilentKeepsWhetherTheRuleIsOn()
    {
        var silent = Request(null) with { IsEnabled = null };

        Assert.False(RouteAnswers.Draft(silent, Held(true) with { IsEnabled = false }).IsEnabled);
        Assert.True(RouteAnswers.Draft(silent, Held(true) with { IsEnabled = true }).IsEnabled);
        Assert.False(RouteAnswers.Draft(Request(null) with { IsEnabled = false }, Held(true)).IsEnabled);
    }

    [Fact]
    public void ARequestThatSaysSoTakesTheHoldOffAndPutsItBack()
    {
        Assert.False(RouteAnswers.Draft(Request(false), Held(true)).HoldsWhenDown);
        Assert.True(RouteAnswers.Draft(Request(true), Held(false)).HoldsWhenDown);
    }

    [Fact]
    public void TheRestOfTheRequestComesThroughAsItIs()
    {
        var draft = RouteAnswers.Draft(Request(null), Held(false));

        Assert.Equal("rule", draft.Name);
        Assert.Equal(RouteAction.Out, draft.Action);
        Assert.Equal("direct", draft.Outbound);
        Assert.Equal(["ifconfig.me", "ipinfo.io"], draft.Targets);
        Assert.True(draft.IsEnabled);
    }

    [Fact]
    public void ARequestThatStaysSilentKeepsTheClientsTheInterfacesAndTheSourcePorts()
    {
        var held = Held(true) with { Clients = ["bor"], Inbounds = ["awg1"], SourcePorts = ["5000"] };

        var draft = RouteAnswers.Draft(Request(null), held);

        Assert.Equal(["bor"], draft.Clients);
        Assert.Equal(["awg1"], draft.Inbounds);
        Assert.Equal(["5000"], draft.SourcePorts);
    }

    [Fact]
    public void ARequestThatNamesTheClientsTheInterfacesAndTheSourcePortsSetsThem()
    {
        var held = Held(true) with { Clients = ["bor"], Inbounds = ["awg1"], SourcePorts = ["5000"] };
        var request = Request(null) with { Clients = ["guest, bor-phone"], Inbounds = [], SourcePorts = ["22"] };

        var draft = RouteAnswers.Draft(request, held);

        Assert.Equal(["guest", "bor-phone"], draft.Clients);
        Assert.Empty(draft.Inbounds);
        Assert.Equal(["22"], draft.SourcePorts);
    }

    [Fact]
    public void ABasicRequestKeepsTheListItStaysSilentAbout()
    {
        var held = new RouteBasic { Direct = ["geoip:ru"], Block = ["geosite:category-ads-all"] };

        var basic = RouteAnswers.Basic(new RouteBasicRequest(null, ["ads.example.com"]), held);

        Assert.Equal(["geoip:ru"], basic.Direct);
        Assert.Equal(["ads.example.com"], basic.Block);
    }

    private static RouteRequest Request(bool? holds) =>
        new("rule", true, RouteAction.Out, "direct", holds, ["ifconfig.me ipinfo.io"], [], [], RouteProtocol.Any);

    private static RouteRule Held(bool holds) =>
        RouteDefaults.Fresh("rule") with { Id = 1, Outbound = "direct", HoldsWhenDown = holds };
}
