using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;

namespace AmneziaGeo.Server.Tests;

public class ClientRequestTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AClientAddedByItsNameAloneIsOn()
    {
        var request = Read("{\"name\":\"anna\"}");

        Assert.True(ClientAnswers.Draft(request, null).IsEnabled);
    }

    [Fact]
    public void AChangeThatNamesNoSwitchKeepsWhatTheClientHeld()
    {
        var on = ClientDefaults.Fresh(1, "anna") with { IsEnabled = true };
        var off = on with { IsEnabled = false };
        var request = Read("{\"configId\":1,\"name\":\"anna\",\"note\":\"desk\"}");

        Assert.True(ClientAnswers.Draft(request, on).IsEnabled);
        Assert.False(ClientAnswers.Draft(request, off).IsEnabled);
    }

    [Fact]
    public void ARequestThatNamesTheSwitchOutweighsWhatTheClientHeld()
    {
        var on = ClientDefaults.Fresh(1, "anna") with { IsEnabled = true };
        var off = on with { IsEnabled = false };

        Assert.False(ClientAnswers.Draft(Read("{\"name\":\"anna\",\"isEnabled\":false}"), null).IsEnabled);
        Assert.False(ClientAnswers.Draft(Read("{\"name\":\"anna\",\"isEnabled\":false}"), on).IsEnabled);
        Assert.True(ClientAnswers.Draft(Read("{\"name\":\"anna\",\"isEnabled\":true}"), off).IsEnabled);
    }

    private static ClientRequest Read(string json) => JsonSerializer.Deserialize<ClientRequest>(json, Web)!;
}
