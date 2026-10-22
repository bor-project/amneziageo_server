using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Dns;

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
    public void AServerThatNeverChoseTakesTheResolverWhileTheRoutingNeedsItAndTheServersOfTheEndpointOtherwise()
    {
        Assert.Equal("10.8.0.1|Server|Routing|True", Line(Client(), null, Panel, Panel));
        Assert.Equal("1.1.1.1|Server|Routing|False", Line(Client(), null, null, Panel));
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
    public void AnAutomaticClientTakesTheChoiceOfItsTemplateAndThenOfTheServer()
    {
        var own = new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] };
        var picked = new ClientTemplate { Name = "picked", DnsMode = DnsChoice.Panel };
        var bare = new ClientTemplate { Name = "bare" };

        Assert.Equal("8.8.8.8|Template|Own|False", Line(Client(), own, Panel, Panel));
        Assert.Equal("10.8.0.1|Template|Panel|True", Line(Client(), picked, null, Panel));
        Assert.Equal("9.9.9.9|Server|Own|False", Line(Client(), bare, Panel, Panel, new DnsFiles(DnsChoice.Own, ["9.9.9.9"])));
        Assert.Equal("10.8.0.1|Server|Panel|True", Line(Client(), bare, null, Panel, new DnsFiles(DnsChoice.Panel, [])));
        Assert.Equal("1.1.1.1|Server|Endpoint|False", Line(Client(), bare, Panel, Panel, new DnsFiles(DnsChoice.Endpoint, [])));
    }

    [Fact]
    public void TheNetworkOfTheDeviceLeavesTheFileWithoutServersAtEveryLevel()
    {
        var network = new DnsFiles(DnsChoice.Network, []);
        var left = new ClientTemplate { Name = "left", DnsMode = DnsChoice.Network };

        Assert.Equal("|Server|Network|False", Line(Client(), null, Panel, Panel, network));
        Assert.Equal("|Template|Network|False", Line(Client(), left, Panel, Panel));
        Assert.Equal("|Client|Network|False", Line(Client() with { DnsMode = DnsChoice.Network }, null, Panel, Panel));
        Assert.DoesNotContain("DNS =", ClientText.Text(Endpoint, Client(), null, Panel, Panel, network), StringComparison.Ordinal);
    }

    [Fact]
    public void AClientOrATemplateTakesTheResolverWhileTheRoutingNeedsItOrTheServersOfTheEndpointOfItsOwnAccord()
    {
        var routing = Client() with { DnsMode = DnsChoice.Routing };
        var endpoint = new ClientTemplate { Name = "endpoint", DnsMode = DnsChoice.Endpoint };
        var panel = new DnsFiles(DnsChoice.Panel, []);

        Assert.Equal("10.8.0.1|Client|Routing|True", Line(routing, null, Panel, Panel, panel));
        Assert.Equal("1.1.1.1|Client|Routing|False", Line(routing, null, null, Panel, panel));
        Assert.Equal("1.1.1.1|Template|Endpoint|False", Line(Client(), endpoint, Panel, Panel, panel));
        Assert.Equal("1.1.1.1,1.0.0.1|Template|Endpoint|False", Line(Client(), endpoint, Panel, Panel, panel, Endpoint with { Dns = [] }));
    }

    [Fact]
    public void ATemplateRoutedByNameTakesTheResolverWhateverTheClientPicks()
    {
        var names = new ClientTemplate { Name = "names", StandIns = true, DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] };
        var own = Client() with { DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] };
        var network = Client() with { DnsMode = DnsChoice.Network };

        Assert.Equal("10.8.0.1|StandIns|Panel|True", Line(own, names, Panel, Panel));
        Assert.Equal("10.8.0.1|StandIns|Panel|True", Line(network, names, Panel, Panel));
    }

    [Fact]
    public void TheFileWritesTheServersTheChoiceGives()
    {
        ClientTemplate?[] templates =
        [
            null,
            new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["8.8.8.8"] },
            new ClientTemplate { Name = "picked", DnsMode = DnsChoice.Panel },
            new ClientTemplate { Name = "left", DnsMode = DnsChoice.Network },
            new ClientTemplate { Name = "names", StandIns = true },
        ];
        TunnelClient[] clients =
        [
            Client(),
            Client() with { DnsMode = DnsChoice.Panel },
            Client() with { DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] },
            Client() with { DnsMode = DnsChoice.Network },
            Client() with { DnsMode = DnsChoice.Routing },
            Client() with { DnsMode = DnsChoice.Endpoint },
        ];
        DnsFiles[] servers = [DnsFiles.Usual, new(DnsChoice.Panel, []), new(DnsChoice.Network, []), new(DnsChoice.Own, ["9.9.9.9"])];

        foreach (var template in templates)
        {
            foreach (var client in clients)
            {
                foreach (var files in servers)
                {
                    foreach (var resolver in new[] { null, Panel })
                    {
                        var line = ClientText.Dns(Endpoint, client, template, resolver, Panel, files);
                        var text = ClientText.Text(Endpoint, client, template, resolver, Panel, files);

                        if (line.Servers.Count > 0)
                        {
                            Assert.Contains($"DNS = {string.Join(", ", line.Servers)}\n", text, StringComparison.Ordinal);
                        }
                        else
                        {
                            Assert.DoesNotContain("DNS =", text, StringComparison.Ordinal);
                        }
                    }
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
        Assert.Equal("bad-client-dns", ClientRules.Check(Client() with { DnsMode = (DnsChoice)6 })?.Code);
        Assert.Null(ClientRules.Check(Client() with { DnsMode = DnsChoice.Panel }));
        Assert.Null(ClientRules.Check(Client() with { DnsMode = DnsChoice.Network }));
        Assert.Null(ClientRules.Check(Client() with { DnsMode = DnsChoice.Endpoint }));
        Assert.Null(ClientRules.Check(Client() with { DnsMode = DnsChoice.Routing }));
        Assert.Equal("bad-dns", TemplateRules.Check(new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own })?.Code);
        Assert.Null(TemplateRules.Check(new ClientTemplate { Name = "left", DnsMode = DnsChoice.Network }));
        Assert.Equal("bad-dns-mode", TemplateRules.Check(new ClientTemplate { Name = "odd", DnsMode = (DnsChoice)9 })?.Code);
    }

    [Fact]
    public void TheServerPicksOneOfItsChoicesAndNamesItsOwnServers()
    {
        var usual = DnsDefaults.Settings;

        Assert.Null(DnsRules.Check(usual));
        Assert.Equal(DnsChoice.Routing, usual.FilesMode);
        Assert.Null(DnsRules.Check(usual with { FilesMode = DnsChoice.Network }));
        Assert.Null(DnsRules.Check(usual with { FilesMode = DnsChoice.Own, FilesDns = ["9.9.9.9", "2620:fe::fe"] }));
        Assert.Equal("bad-dns", DnsRules.Check(usual with { FilesMode = DnsChoice.Own })?.Code);
        Assert.Equal("bad-dns", DnsRules.Check(usual with { FilesMode = DnsChoice.Own, FilesDns = ["dns.quad9.net"] })?.Code);
        Assert.Equal("bad-dns-mode", DnsRules.Check(usual with { FilesMode = DnsChoice.Auto })?.Code);
        Assert.Equal("bad-dns-mode", DnsRules.Check(usual with { FilesMode = (DnsChoice)7 })?.Code);
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
        var network = ClientAnswers.Draft(new ClientRequest(1, "anna", null, null, null, ["10.8.0.2/32"], null, null, DnsMode: "network"), held);
        var listed = TemplateAnswers.Draft(new TemplateRequest("own", [], ["9.9.9.9"], null, null));
        var bare = TemplateAnswers.Draft(new TemplateRequest("bare", [], [], null, null));
        var picked = TemplateAnswers.Draft(new TemplateRequest("picked", [], [], null, null, DnsMode: "panel"));
        var routing = TemplateAnswers.Draft(new TemplateRequest("routing", [], [], null, null, DnsMode: "routing"));

        Assert.Equal(DnsChoice.Panel, kept.DnsMode);
        Assert.Equal("Own 9.9.9.9", $"{own.DnsMode} {string.Join(",", own.Dns)}");
        Assert.Equal((DnsChoice)(-1), odd.DnsMode);
        Assert.Equal(DnsChoice.Network, network.DnsMode);
        Assert.Equal(DnsChoice.Own, listed.DnsMode);
        Assert.Equal(DnsChoice.Auto, bare.DnsMode);
        Assert.Equal(DnsChoice.Panel, picked.DnsMode);
        Assert.Equal(DnsChoice.Routing, routing.DnsMode);
        Assert.Equal(
            ["auto", "panel", "own", "network", "endpoint", "routing"],
            new[] { DnsChoice.Auto, DnsChoice.Panel, DnsChoice.Own, DnsChoice.Network, DnsChoice.Endpoint, DnsChoice.Routing }.Select(DnsChoiceName.Of));
    }

    [Fact]
    public void ARequestOfTheResolverThatNamesNoChoiceOfTheFilesKeepsTheOneHeld()
    {
        var held = DnsDefaults.Settings with { FilesMode = DnsChoice.Own, FilesDns = ["9.9.9.9"] };

        var kept = DnsAnswers.Draft(Request(null, null), held);
        var network = DnsAnswers.Draft(Request("network", null), held);
        var auto = DnsAnswers.Draft(Request("auto", null), held);
        var own = DnsAnswers.Draft(Request("own", ["149.112.112.112", "9.9.9.9"]), held);
        var fresh = DnsAnswers.Draft(Request(null, null));

        Assert.Equal("Own 9.9.9.9", $"{kept.FilesMode} {string.Join(",", kept.FilesDns)}");
        Assert.Equal(DnsChoice.Network, network.FilesMode);
        Assert.Equal(DnsChoice.Routing, auto.FilesMode);
        Assert.Equal("Own 149.112.112.112,9.9.9.9", $"{own.FilesMode} {string.Join(",", own.FilesDns)}");
        Assert.Equal(DnsChoice.Routing, fresh.FilesMode);
    }

    [Fact]
    public async Task TheChoicesAreWrittenDownAndReadBack()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;

        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] }, ct)).Record!;
        var template = (await bench.Templates.AddAsync(
            new ClientTemplate { Name = "own", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"] },
            ct)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "anna") with { Address = ["10.8.0.2/32"], DnsMode = DnsChoice.Network },
            ct)).Record!;
        var saved = await bench.Resolver.SaveAsync(DnsDefaults.Settings with { FilesMode = DnsChoice.Own, FilesDns = ["9.9.9.9", "2620:fe::fe"] }, ct);

        var kept = await bench.Templates.FindAsync(template.Id, ct);
        var read = await bench.Clients.FindAsync(client.Id, ct);
        var settings = await bench.Resolver.ReadAsync(ct);

        Assert.True(saved.IsOk);
        Assert.Equal(DnsChoice.Own, kept!.DnsMode);
        Assert.Equal(DnsChoice.Network, read!.DnsMode);
        Assert.Equal("Own 9.9.9.9,2620:fe::fe", $"{settings.FilesMode} {string.Join(",", settings.FilesDns)}");
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
        var before = holders.Picked;
        await bench.Resolver.SaveAsync(DnsDefaults.Settings with { FilesMode = DnsChoice.Panel }, ct);
        await holders.RefreshAsync(bench.Services, ct);

        Assert.Equal(2, before);
        Assert.Equal(3, holders.Picked);
    }

    private static string Line(
        TunnelClient client,
        ClientTemplate? template,
        IReadOnlyList<string>? resolver,
        IReadOnlyList<string>? panel,
        DnsFiles? files = null,
        ServerConfig? config = null)
    {
        var line = ClientText.Dns(config ?? Endpoint, client, template, resolver, panel, files);

        return $"{string.Join(",", line.Servers)}|{line.From}|{line.Choice}|{line.IsPanel}";
    }

    private static TunnelClient Client() => ClientDefaults.Fresh(1, "anna") with { Address = ["10.8.0.2/32"] };

    private static DnsRequest Request(string? mode, string[]? servers) =>
        new(true, 53, ["https://1.1.1.1/dns-query"], string.Empty, [], 60, 1000, 0, 3600, mode, servers);
}
