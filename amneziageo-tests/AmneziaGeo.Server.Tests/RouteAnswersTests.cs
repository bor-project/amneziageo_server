using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RouteAnswersTests
{
    [Fact]
    public void ARuleThatIsAddedStaysOffUnlessTheRequestSaysOtherwise()
    {
        Assert.False(RouteAnswers.Draft(Request() with { IsEnabled = null }).IsEnabled);
        Assert.True(RouteAnswers.Draft(Request() with { IsEnabled = true }).IsEnabled);
        Assert.False(RouteAnswers.Draft(Request() with { IsEnabled = false }).IsEnabled);
    }

    [Fact]
    public void ARequestThatStaysSilentKeepsWhetherTheRuleIsOn()
    {
        var silent = Request() with { IsEnabled = null };

        Assert.False(RouteAnswers.Draft(silent, Held() with { IsEnabled = false }).IsEnabled);
        Assert.True(RouteAnswers.Draft(silent, Held() with { IsEnabled = true }).IsEnabled);
        Assert.False(RouteAnswers.Draft(Request() with { IsEnabled = false }, Held()).IsEnabled);
    }

    [Fact]
    public void TheRestOfTheRequestComesThroughAsItIs()
    {
        var draft = RouteAnswers.Draft(Request(), Held());

        Assert.Equal("rule", draft.Name);
        Assert.Equal(RouteAction.Out, draft.Action);
        Assert.Equal("direct", draft.Outbound);
        Assert.Equal(["ifconfig.me", "ipinfo.io"], draft.Targets);
        Assert.True(draft.IsEnabled);
    }

    [Fact]
    public void ARequestThatStaysSilentKeepsTheClientsTheInterfacesAndTheSourcePorts()
    {
        var held = Held() with { Clients = ["bor"], Inbounds = ["awg1"], SourcePorts = ["5000"] };

        var draft = RouteAnswers.Draft(Request(), held);

        Assert.Equal(["bor"], draft.Clients);
        Assert.Equal(["awg1"], draft.Inbounds);
        Assert.Equal(["5000"], draft.SourcePorts);
    }

    [Fact]
    public void ARequestThatNamesTheClientsTheInterfacesAndTheSourcePortsSetsThem()
    {
        var held = Held() with { Clients = ["bor"], Inbounds = ["awg1"], SourcePorts = ["5000"] };
        var request = Request() with { Clients = ["guest, bor-phone"], Inbounds = [], SourcePorts = ["22"] };

        var draft = RouteAnswers.Draft(request, held);

        Assert.Equal(["guest", "bor-phone"], draft.Clients);
        Assert.Empty(draft.Inbounds);
        Assert.Equal(["22"], draft.SourcePorts);
    }

    [Fact]
    public void ARequestThatStillNamesClientAddressesIsRefused()
    {
        Assert.Equal("sources-gone", RouteAnswers.Dropped(Request() with { Sources = ["10.8.0.0/24"] })?.Code);
        Assert.Null(RouteAnswers.Dropped(Request() with { Sources = [] }));
        Assert.Null(RouteAnswers.Dropped(Request()));
    }

    [Fact]
    public void ABasicRequestKeepsTheListItStaysSilentAbout()
    {
        var held = new RouteBasic { Direct = ["geoip:ru"], Block = ["geosite:category-ads-all"] };

        var basic = RouteAnswers.Basic(new RouteBasicRequest(null, ["ads.example.com"]), held);

        Assert.Equal(["geoip:ru"], basic.Direct);
        Assert.Equal(["ads.example.com"], basic.Block);
    }

    private static RouteRequest Request() =>
        new("rule", true, RouteAction.Out, "direct", ["ifconfig.me ipinfo.io"], [], RouteProtocol.Any);

    private static RouteRule Held() => RouteDefaults.Fresh("rule") with { Id = 1, Outbound = "direct" };
}
