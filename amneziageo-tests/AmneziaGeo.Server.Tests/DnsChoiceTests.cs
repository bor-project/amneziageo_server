using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Tests;

public class DnsChoiceTests
{
    private static readonly string[] Panel = ["10.8.0.1"];

    private static readonly ServerConfig Endpoint = new()
    {
        Id = 1,
        Name = "awg1",
        Address = ["10.8.0.1/24"],
        Dns = ["1.1.1.1"],
        AllowedIps = ["0.0.0.0/0"],
        PrivateKey = "kEnCIRhKjNllB2d7J7t5gMAySOXbC9KKrcPwIbkgGUc=",
        PublicKey = "6isSFA1FOVjJl9Ub1VvoJ8OKXFgLPFcwJjzLU+AWSlE=",
    };

    [Fact]
    public void AnAutomaticClientTakesTheResolverWhileTheRoutingNeedsItAndTheServersOfTheEndpointOtherwise()
    {
        Assert.Equal("10.8.0.1|Endpoint|Auto|True", Line(Client(), null, Panel, Panel));
        Assert.Equal("1.1.1.1|Endpoint|Auto|False", Line(Client(), null, null, Panel));
    }

    [Fact]
    public void AClientThatPicksTheResolverTakesItWhetherTheRoutingNeedsItOrNot()
    {
        var picked = Client() with { DnsMode = DnsChoice.Panel };

        Assert.Equal("10.8.0.1|Client|Panel|True", Line(picked, null, null, Panel));
        Assert.Equal("1.1.1.1|Client|Panel|False", Line(picked, null, null, []));
    }

    [Fact]
    public void ServersOfItsOwnOutweighTheResolver()
    {
        var own = Client() with { DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] };

        Assert.Equal("9.9.9.9|Client|Own|False", Line(own, null, Panel, Panel));
    }

    [Fact]
    public void AnAutomaticClientTakesTheChoiceOfItsTemplateAndThenOfItsEndpoint()
    {
        var own = new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] };
        var picked = new ClientTemplate { Name = "picked", DnsMode = DnsChoice.Panel };
        var bare = new ClientTemplate { Name = "bare" };

        Assert.Equal("8.8.8.8|Template|Own|False", Line(Client(), own, Panel, Panel));
        Assert.Equal("10.8.0.1|Template|Panel|True", Line(Client(), picked, null, Panel));
        Assert.Equal("1.1.1.1|Endpoint|Own|False", Line(Client(), bare, Panel, Panel, Endpoint with { DnsMode = DnsChoice.Own }));
        Assert.Equal("10.8.0.1|Endpoint|Panel|True", Line(Client(), bare, null, Panel, Endpoint with { DnsMode = DnsChoice.Panel }));
    }

    [Fact]
    public void ATemplateRoutedByNameTakesTheResolverWhateverTheClientPicks()
    {
        var names = new ClientTemplate { Name = "names", StandIns = true, DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] };
        var own = Client() with { DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] };

        Assert.Equal("10.8.0.1|StandIns|Panel|True", Line(own, names, Panel, Panel));
    }

    [Fact]
    public void TheFileWritesTheServersTheChoiceGives()
    {
        ClientTemplate?[] templates =
        [
            null,
            new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] },
            new ClientTemplate { Name = "picked", DnsMode = DnsChoice.Panel },
            new ClientTemplate { Name = "names", StandIns = true },
        ];
        TunnelClient[] clients =
        [
            Client(),
            Client() with { DnsMode = DnsChoice.Panel },
            Client() with { DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] },
        ];

        foreach (var template in templates)
        {
            foreach (var client in clients)
            {
                foreach (var resolver in new[] { null, Panel })
                {
                    var line = ClientText.Dns(Endpoint, client, template, resolver, Panel);
                    var text = ClientText.Text(Endpoint, client, template, resolver, Panel);

                    Assert.Contains($"DNS = {string.Join(", ", line.Servers)}\n", text, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void ServersOfItsOwnMustBeNamedAndBeAddresses()
    {
        Assert.Equal("bad-client-dns", ClientRules.Check(Client() with { DnsMode = DnsChoice.Own })?.Code);
        Assert.Equal("bad-client-dns", ClientRules.Check(Client() with { DnsMode = DnsChoice.Own, Dns = ["dns.google"] })?.Code);
        Assert.Equal("bad-client-dns", ClientRules.Check(Client() with { DnsMode = (DnsChoice)(-1) })?.Code);
        Assert.Null(ClientRules.Check(Client() with { DnsMode = DnsChoice.Panel }));
        Assert.Equal("bad-dns", TemplateRules.Check(new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own })?.Code);
        Assert.Null(ConfigRules.Check(Fresh() with { DnsMode = DnsChoice.Panel }));
        Assert.Equal("bad-dns", ConfigRules.Check(Fresh() with { DnsMode = DnsChoice.Own, Dns = [] })?.Code);
        Assert.Equal("bad-dns-mode", ConfigRules.Check(Fresh() with { DnsMode = (DnsChoice)(-1) })?.Code);
    }

    [Fact]
    public void ARequestThatNamesNoChoiceKeepsTheOneHeldAndATemplateWithServersPicksThem()
    {
        var held = Client() with { DnsMode = DnsChoice.Panel };

        var kept = ClientAnswers.Draft(new ClientRequest(1, "anna", null, null, null, ["10.8.0.2/32"], null, null), held);
        var own = ClientAnswers.Draft(
            new ClientRequest(1, "anna", null, null, null, ["10.8.0.2/32"], null, null, DnsMode: "own", Dns: ["9.9.9.9"]),
            held);
        var odd = ClientAnswers.Draft(new ClientRequest(1, "anna", null, null, null, ["10.8.0.2/32"], null, null, DnsMode: "dhcp"), held);
        var listed = TemplateAnswers.Draft(new TemplateRequest("own", [], ["9.9.9.9"], null, null));
        var bare = TemplateAnswers.Draft(new TemplateRequest("bare", [], [], null, null));
        var picked = TemplateAnswers.Draft(new TemplateRequest("picked", [], [], null, null, DnsMode: "panel"));
        var endpoint = ConfigAnswers.Draft(Request(null), Endpoint with { DnsMode = DnsChoice.Own });
        var chosen = ConfigAnswers.Draft(Request("panel"), Endpoint);

        Assert.Equal(DnsChoice.Panel, kept.DnsMode);
        Assert.Equal("Own 9.9.9.9", $"{own.DnsMode} {string.Join(",", own.Dns)}");
        Assert.Equal((DnsChoice)(-1), odd.DnsMode);
        Assert.Equal(DnsChoice.Own, listed.DnsMode);
        Assert.Equal(DnsChoice.Auto, bare.DnsMode);
        Assert.Equal(DnsChoice.Panel, picked.DnsMode);
        Assert.Equal(DnsChoice.Own, endpoint.DnsMode);
        Assert.Equal(DnsChoice.Panel, chosen.DnsMode);
        Assert.Equal(["auto", "panel", "own"], new[] { DnsChoice.Auto, DnsChoice.Panel, DnsChoice.Own }.Select(DnsChoiceName.Of));
    }

    [Fact]
    public async Task TheChoicesAreWrittenDownAndReadBack()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;

        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"], DnsMode = DnsChoice.Panel },
            ct)).Record!;
        var template = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] },
            ct)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "anna") with { Address = ["10.8.0.2/32"], DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] },
            ct)).Record!;

        var config = await bench.Configs.FindAsync(endpoint.Id, ct);
        var kept = await bench.Templates.FindAsync(template.Id, ct);
        var read = await bench.Clients.FindAsync(client.Id, ct);

        Assert.Equal(DnsChoice.Panel, config!.DnsMode);
        Assert.Equal(DnsChoice.Own, kept!.DnsMode);
        Assert.Equal("Own 8.8.8.8", $"{read!.DnsMode} {string.Join(",", read.Dns)}");
    }

    [Fact]
    public async Task TheClientsThatPickTheResolverAreCountedWhileTheyAreOn()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] }, ct)).Record!;
        var picked = (await bench.Templates.AddAsync(new ClientTemplate { Name = "picked", DnsMode = DnsChoice.Panel }, ct)).Record!;
        await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "anna") with { Address = ["10.8.0.2/32"], DnsMode = DnsChoice.Panel },
            ct);
        await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "guest") with { Address = ["10.8.0.3/32"] }, ct);
        await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "kiosk") with { Address = ["10.8.0.4/32"], TemplateId = picked.Id },
            ct);
        await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "tablet") with { Address = ["10.8.0.5/32"], DnsMode = DnsChoice.Panel, IsEnabled = false },
            ct);

        var holders = new DnsHolders();
        await holders.RefreshAsync(bench.Services, ct);

        Assert.Equal(2, holders.Picked);
    }

    private static string Line(
        TunnelClient client,
        ClientTemplate? template,
        IReadOnlyList<string>? resolver,
        IReadOnlyList<string>? panel,
        ServerConfig? config = null)
    {
        var line = ClientText.Dns(config ?? Endpoint, client, template, resolver, panel);

        return $"{string.Join(",", line.Servers)}|{line.From}|{line.Choice}|{line.IsPanel}";
    }

    private static ServerConfig Fresh() => ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] };

    private static TunnelClient Client() => ClientDefaults.Fresh(1, "anna") with { Address = ["10.8.0.2/32"] };

    private static ConfigRequest Request(string? mode) =>
        new("awg1", string.Empty, 51820, ["10.8.0.1/24"], ["1.1.1.1"], [], 0, 0, true, true, false, [], string.Empty, string.Empty, null, DnsMode: mode);
}
