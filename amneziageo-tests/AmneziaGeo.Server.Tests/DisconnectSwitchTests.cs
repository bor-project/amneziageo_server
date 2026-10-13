using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Tests;

public class DisconnectSwitchTests
{
    [Fact]
    public void AClientIsTakenDownByTheSignalUnlessThePanelSaysOtherwise()
    {
        var held = ClientDefaults.Fresh(1, "milena") with { Disconnect = false };
        var request = new ClientRequest(1, "milena", held.PrivateKey, null, null, ["10.8.0.2/32"], true, null);

        Assert.True(ClientDefaults.Fresh(1, "milena").Disconnect);
        Assert.True(ClientAnswers.Draft(request, null).Disconnect);
        Assert.False(ClientAnswers.Draft(request, held).Disconnect);
        Assert.True(ClientAnswers.Draft(request with { Disconnect = true }, held).Disconnect);
        Assert.False(ClientAnswers.Draft(request with { Disconnect = false }, null).Disconnect);
    }

    [Fact]
    public void ARequestNamesTheSwitchAndTheAnswerNamesWhatTheApplicationSaid()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var client = ClientDefaults.Fresh(1, "milena") with { Disconnect = false, DisconnectRefused = true };

        var request = System.Text.Json.JsonSerializer.Deserialize<ClientRequest>(
            "{\"configId\":1,\"name\":\"milena\",\"isEnabled\":true,\"disconnect\":false}", web);
        var answer = ClientAnswers.Client(client, "awg1", ClientState.Missing(client), false, [], ClientTraffic.None);
        var fresh = ClientAnswers.Client(ClientDefaults.Fresh(1, "anna"), "awg1", ClientState.Missing(client), false, [], ClientTraffic.None);

        Assert.False(request?.Disconnect);
        Assert.False(answer.Disconnect);
        Assert.True(answer.DisconnectRefused);
        Assert.True(fresh.Disconnect);
        Assert.False(fresh.DisconnectRefused);
    }

    [Fact]
    public async Task TheSwitchIsKeptWithTheClientAndAnEditLeavesWhatTheApplicationSaid()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "milena") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None)).Record!;

        await bench.Clients.NoteRefusalAsync(client.Id, true, CancellationToken.None);
        var said = await bench.Clients.FindAsync(client.Id, CancellationToken.None);
        await bench.Clients.ChangeAsync(client.Id, client with { Disconnect = false, Note = "desk" }, CancellationToken.None);
        var edited = await bench.Clients.FindAsync(client.Id, CancellationToken.None);
        await bench.Clients.NoteRefusalAsync(client.Id, false, CancellationToken.None);
        var taken = await bench.Clients.FindAsync(client.Id, CancellationToken.None);

        Assert.True(client.Disconnect);
        Assert.False(client.DisconnectRefused);
        Assert.True(said?.Disconnect);
        Assert.True(said?.DisconnectRefused);
        Assert.Equal(client.UpdatedUtc, said?.UpdatedUtc);
        Assert.False(edited?.Disconnect);
        Assert.True(edited?.DisconnectRefused);
        Assert.Equal("desk", edited?.Note);
        Assert.False(taken?.DisconnectRefused);
    }
}
