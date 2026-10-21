using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class DnsConflictsTests
{
    private static readonly DnsSettings On = DnsDefaults.Settings with { IsEnabled = true };

    private static readonly DnsSettings Off = DnsDefaults.Settings with { IsEnabled = false };

    [Fact]
    public void WithTheResolverOffTheRulesTheTemplatesTheFilesAndThePicksThatNeedItAreNamed()
    {
        var world = new DnsWorld
        {
            Settings = Off,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates = [new ClientTemplate { Id = 2, Name = "names", StandIns = true }],
            Clients =
            [
                Client(1, 1, "anna"),
                Client(2, 1, "guest") with { DnsMode = DnsChoice.Panel },
                Client(3, 1, "kiosk") with { TemplateId = 2 },
                Client(4, 1, "tablet"),
            ],
            Holders = [4],
            Rules = [Rule(1, "yt", "anna"), Rule(RouteBasic.BlockId, RouteBasic.BlockName)],
        };

        Assert.Equal(
            [
                "holders-off:error:dns0:tablet",
                "panel-off:warning:dns0,client2:guest",
                "rule-off:error:dns0,basic-1:anna,guest,kiosk,tablet",
                "rule-off:error:dns0,rule1:anna",
                "standins-off:error:dns0,template2:kiosk",
            ],
            Said(DnsConflicts.Of(world)));
    }

    [Fact]
    public void ARuleByNameIsUnreliableForAClientWhoseNameServersLieOutsideTheTunnel()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates =
            [
                new ClientTemplate { Id = 1, Name = "own", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"], AllowedIps = ["203.0.113.0/24"] },
                new ClientTemplate { Id = 2, Name = "full", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"], AllowedIps = ["0.0.0.0/0"] },
            ],
            Clients =
            [
                Client(1, 1, "guest") with { TemplateId = 1 },
                Client(2, 1, "tablet") with { TemplateId = 2 },
                Client(3, 1, "anna"),
            ],
            Rules = [Rule(1, "yt")],
        };

        var report = DnsConflicts.Of(world);
        var guest = Assert.Single(Assert.Single(report.Findings).Clients);

        Assert.Equal(["rule-outside:warning:rule1,template1:guest"], Said(report));
        Assert.Equal("template own 9.9.9.9", $"{guest.From} {guest.Source} {string.Join(",", guest.Servers)}");
    }

    [Fact]
    public void APortOtherThan53KeepsTheResolverOutOfTheFiles()
    {
        var world = new DnsWorld
        {
            Settings = On with { Port = 5353 },
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Clients = [Client(1, 1, "anna") with { DnsMode = DnsChoice.Panel }, Client(2, 1, "guest")],
        };

        Assert.Equal(["port:warning:dns0:anna"], Said(DnsConflicts.Of(world)));
    }

    [Fact]
    public void AnInterfaceTheFirewallKeepsFromTheResolverOrItDoesNotListenOnNamesItsClients()
    {
        var kept = new DnsWorld
        {
            Settings = On,
            KeptOut = ["awg2"],
            Configs = [Config(1, "awg1", "10.8.0.1/24"), Config(2, "awg2", "10.8.1.1/24")],
            Clients =
            [
                Client(1, 1, "anna") with { DnsMode = DnsChoice.Panel },
                Client(2, 2, "guest") with { DnsMode = DnsChoice.Panel },
                Client(3, 2, "kiosk"),
            ],
            Rules = [Rule(1, "yt")],
        };
        var deaf = kept with { KeptOut = [], Settings = On with { Listen = ["10.8.0.1"] } };

        Assert.Equal(["kept:warning:dns0,config2:guest,kiosk"], Said(DnsConflicts.Of(kept)));
        Assert.Equal(["listen:warning:dns0,config2:guest,kiosk"], Said(DnsConflicts.Of(deaf)));
    }

    [Fact]
    public void TheNeedsNameTheRulesTheTemplatesThePicksAndTheFiles()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates = [new ClientTemplate { Id = 2, Name = "names", StandIns = true }],
            Clients =
            [
                Client(1, 1, "anna") with { DnsMode = DnsChoice.Panel },
                Client(2, 1, "guest"),
                Client(3, 1, "tablet"),
                Client(4, 1, "idle") with { DnsMode = DnsChoice.Panel, IsEnabled = false },
            ],
            Holders = [3, 4],
            Rules = [Rule(1, "yt")],
        };

        var needs = DnsConflicts.Of(world).Needs;

        Assert.Equal(
            "rules yt; templates names; picked anna; holders tablet",
            $"rules {Names(needs.Rules)}; templates {Names(needs.Templates)}; picked {string.Join(",", needs.Picked.Select(one => one.Name))}; holders {string.Join(",", needs.Holders.Select(one => one.Name))}");
    }

    [Fact]
    public void ANameServerOutsideTheTunnelWithoutRulesIsOnlyAHint()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates = [new ClientTemplate { Id = 1, Name = "own", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"], AllowedIps = ["203.0.113.0/24"] }],
            Clients = [Client(1, 1, "guest") with { TemplateId = 1 }],
        };

        Assert.Empty(DnsConflicts.Of(world).Findings);
        Assert.Equal(["outside:hint:client1:guest"], Said(DnsConflicts.Of(world, hints: true)));
    }

    [Fact]
    public void TheLineOfAClientIsTheOneItsFileNames()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Clients = [Client(1, 1, "anna") with { DnsMode = DnsChoice.Panel }, Client(2, 1, "guest")],
        };

        var picked = DnsConflicts.Line(world, world.Clients[0]);
        var plain = DnsConflicts.Line(world, world.Clients[1]);

        Assert.Equal("10.8.0.1 Client Panel True", $"{string.Join(",", picked!.Servers)} {picked.From} {picked.Choice} {picked.IsPanel}");
        Assert.Equal("1.1.1.1,1.0.0.1 Endpoint Auto False", $"{string.Join(",", plain!.Servers)} {plain.From} {plain.Choice} {plain.IsPanel}");
    }

    [Fact]
    public void ADraftIsCheckedInTheWorldItWouldJoin()
    {
        var world = new DnsWorld
        {
            Settings = Off,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates = [new ClientTemplate { Id = 1, Name = "own", DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"], AllowedIps = ["203.0.113.0/24"] }],
            Clients = [Client(1, 1, "guest") with { TemplateId = 1 }],
        };

        var client = DnsChecks.Check(
            world,
            new DnsCheckRequest(Client: new ClientRequest(1, "anna", null, null, null, ["10.8.0.3/32"], null, null, DnsMode: "panel")));
        var rule = DnsChecks.Check(
            world with { Settings = On },
            new DnsCheckRequest(Rule: new RouteRequest("yt", true, "out", "finland", ["youtube.com"], null, null)));
        var dns = DnsChecks.Check(world with { Holders = [1] }, new DnsCheckRequest(Dns: new DnsRequest(false, 53, null, null, null, 60, 1000, 60, 3600)));

        Assert.Equal(
            "1.1.1.1,1.0.0.1 client panel False awg1",
            $"{string.Join(",", client.Line!.Servers)} {client.Line.From} {client.Line.Choice} {client.Line.IsPanel} {client.Endpoint}");
        Assert.Equal(["panel-off:warning:dns0,client0:anna"], Said(client.Findings));
        Assert.Equal(["rule-outside:warning:rule0,template1:guest"], Said(rule.Findings));
        Assert.Equal(["holders-off:error:dns0:guest"], Said(dns.Findings));
    }

    [Fact]
    public void ATemplateDraftKeepsTheRangesOfTheTemplateItChanges()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Templates =
            [
                new ClientTemplate
                {
                    Id = 1,
                    Name = "own",
                    Entries = TemplateAnswers.Entries(["203.0.113.0/24"]),
                    DnsMode = DnsChoice.Own,
                    Dns = ["9.9.9.9"],
                    AllowedIps = ["203.0.113.0/24"],
                },
            ],
            Clients = [Client(1, 1, "guest") with { TemplateId = 1 }],
            Rules = [Rule(1, "yt")],
        };
        var request = new DnsCheckRequest(1, Template: new TemplateRequest("own", ["203.0.113.0/24"], ["9.9.9.9"], null, null, DnsMode: "own"));

        var kept = DnsChecks.Check(world, request);
        var widened = DnsChecks.Check(world, request, ["0.0.0.0/0"]);

        Assert.Equal(["rule-outside:warning:rule1,template1:guest"], Said(kept.Findings));
        Assert.Equal("9.9.9.9 template own False", $"{string.Join(",", kept.Line!.Servers)} {kept.Line.From} {kept.Line.Choice} {kept.Line.IsPanel}");
        Assert.Empty(widened.Findings);
    }

    [Fact]
    public void ATemplateCheckNamesTheLineAtEachEndpointOfItsClientsAndThoseThatLoseTheResolver()
    {
        var open = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24"), Config(2, "awg2", "10.8.1.1/24"), Config(3, "awg3", "10.8.2.1/24")],
            Templates = [new ClientTemplate { Id = 2, Name = "names", StandIns = true }],
            Clients = [Client(1, 2, "guest") with { TemplateId = 2 }, Client(2, 1, "anna") with { TemplateId = 2 }],
        };
        var kept = open with { KeptOut = ["awg2"] };
        var shut = open with { KeptOut = ["awg1", "awg2"] };
        var draft = new TemplateRequest("names", ["youtube.com"], [], null, null, StandIns: true);
        var held = new DnsCheckRequest(2, Template: draft);

        Assert.Equal(["awg1 10.8.0.1 standins", "awg2 10.8.1.1 standins"], Lines(DnsChecks.Check(open, held)));
        Assert.Equal(["awg1 10.8.0.1 standins", "awg2 1.1.1.1,1.0.0.1 lost"], Lines(DnsChecks.Check(kept, held)));
        Assert.Equal("awg1", DnsChecks.Check(kept, held).Line!.Endpoint);
        Assert.Equal(["kept:warning:dns0,config2:guest"], Said(DnsChecks.Check(kept, held).Findings));
        Assert.Equal(["awg1 1.1.1.1,1.0.0.1 lost", "awg2 1.1.1.1,1.0.0.1 lost"], Lines(DnsChecks.Check(shut, held)));
        Assert.Equal(
            ["awg1 10.8.0.1 standins", "awg2 10.8.1.1 standins", "awg3 10.8.2.1 standins"],
            Lines(DnsChecks.Check(open, new DnsCheckRequest(null, Template: draft))));
    }

    [Fact]
    public void ATemplateCheckTakesTheClientsTheDraftNames()
    {
        var world = new DnsWorld
        {
            Settings = On,
            Configs = [Config(1, "awg1", "10.8.0.1/24"), Config(2, "awg2", "10.8.1.1/24")],
            Templates = [new ClientTemplate { Id = 2, Name = "names", StandIns = true }],
            Clients = [Client(1, 2, "guest") with { TemplateId = 2 }, Client(2, 1, "anna")],
        };
        var draft = new TemplateRequest("names", ["youtube.com"], [], null, null, StandIns: true, Clients: [2]);

        Assert.Equal(["awg1 10.8.0.1 standins"], Lines(DnsChecks.Check(world, new DnsCheckRequest(2, Template: draft))));
        Assert.Equal(["awg1 10.8.0.1 standins"], Lines(DnsChecks.Check(world, new DnsCheckRequest(null, Template: draft))));
        Assert.Equal(
            ["awg2 10.8.1.1 standins"],
            Lines(DnsChecks.Check(world, new DnsCheckRequest(2, Template: draft with { Clients = null }))));
    }

    [Fact]
    public void AClientCheckTakesTheRulesTheDraftNames()
    {
        var world = new DnsWorld
        {
            Settings = Off,
            Configs = [Config(1, "awg1", "10.8.0.1/24")],
            Clients = [Client(1, 1, "anna"), Client(2, 1, "bob")],
            Rules = [Rule(1, "yt", "anna")],
        };
        var draft = new ClientRequest(1, "bob", null, null, null, null, null, null, Rules: [1]);

        Assert.Equal(["rule-off:error:dns0,rule1:anna,bob"], Said(DnsChecks.Check(world, new DnsCheckRequest(2, Client: draft)).Findings));
        Assert.Empty(DnsChecks.Check(world, new DnsCheckRequest(2, Client: draft with { Rules = null })).Findings);
    }

    [Fact]
    public void AnEndpointCheckTakesTheRulesTheDraftNames()
    {
        var world = new DnsWorld
        {
            Settings = Off,
            Configs = [Config(1, "awg1", "10.8.0.1/24"), Config(2, "awg2", "10.8.1.1/24")],
            Clients = [Client(1, 1, "anna"), Client(3, 2, "carl")],
            Rules = [Rule(1, "yt") with { Inbounds = ["awg1"] }],
        };
        var draft = new ConfigRequest("awg2", null, 0, ["10.8.1.1/24"], null, ["0.0.0.0/0"], 0, 0, true, null, null, null, null, null, null, Rules: [1]);

        var named = DnsChecks.Check(world, new DnsCheckRequest(2, Config: draft));
        var kept = DnsChecks.Check(world, new DnsCheckRequest(2, Config: draft with { Rules = null }));

        Assert.Contains(named.Findings, one => one.Clients.Any(client => client.Name == "carl"));
        Assert.DoesNotContain(kept.Findings, one => one.Clients.Any(client => client.Name == "carl"));
    }

    private static IReadOnlyList<string> Said(DnsReport report) => Said(report.Findings);

    private static IReadOnlyList<string> Lines(DnsCheckResponse check) =>
        [.. check.Lines.Select(one => $"{one.Endpoint} {string.Join(",", one.Servers)} {(one.Lost ? "lost" : one.From)}")];

    private static IReadOnlyList<string> Said(IEnumerable<DnsFinding> findings) =>
        [
            .. findings
                .Select(one => $"{one.Code}:{one.Level}:{string.Join(",", one.Subjects.Select(subject => $"{subject.Kind}{subject.Id}"))}:{string.Join(",", one.Clients.Select(client => client.Name))}")
                .Order(StringComparer.Ordinal),
        ];

    private static string Names(IReadOnlyList<DnsSubject> subjects) => string.Join(",", subjects.Select(one => one.Name));

    private static ServerConfig Config(long id, string name, string network) =>
        ConfigDefaults.Fresh(name) with { Id = id, Address = [network], AllowedIps = ["0.0.0.0/0"], Dns = ["1.1.1.1", "1.0.0.1"] };

    private static TunnelClient Client(long id, long config, string name) =>
        ClientDefaults.Fresh(config, name) with { Id = id, Address = [$"10.8.{config - 1}.{id + 1}/32"] };

    private static RouteRule Rule(long id, string name, params string[] clients) =>
        RouteDefaults.Fresh(name) with { Id = id, Targets = ["youtube.com"], Clients = clients };
}
