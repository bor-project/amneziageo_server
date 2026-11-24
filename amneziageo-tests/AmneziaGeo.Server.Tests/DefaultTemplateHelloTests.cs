using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public partial class ServiceTests
{
    [Fact]
    public async Task AnUnassignedClientReceivesTheDefaultProxyInAuthenticatedHello()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var proxies = new TransportProxyStore(bench.Db);
        var proxy = await proxies.SaveAsync(0, new TransportProxyEntity
        {
            Name = "front", Hosted = false, Host = "proxy.example", Port = 443, Path = "front",
        }, default).ConfigureAwait(false);
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { ProxyIds = [proxy.Id] }, null, default).ConfigureAwait(false)).IsOk);
        var point = new ServicePoint(endpoint.ListenPort,
            [new ServiceEndpoint(endpoint.Id, endpoint.Name, false, 0, endpoint.ListenPort, "")], "", "");
        var token = Token(client.PrivateKey, endpoint.PublicKey, bench.Clock.Now);
        var answer = await AskAsync(Desk(bench, new SpeedTickets(bench.Clock)), point, token).ConfigureAwait(false);
        Assert.Equal(200, answer.Status);
        var features = Offered(client, endpoint, token, answer);
        Assert.Equal(proxy.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Assert.Single(features.GetProperty("proxies").GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        await bench.Templates.ResetDefaultAsync(default).ConfigureAwait(false);
        token = Token(client.PrivateKey, endpoint.PublicKey, bench.Clock.Now);
        answer = await AskAsync(Desk(bench, new SpeedTickets(bench.Clock)), point, token).ConfigureAwait(false);
        Assert.Empty(Offered(client, endpoint, token, answer).GetProperty("proxies").GetProperty("items").EnumerateArray());
    }
}
