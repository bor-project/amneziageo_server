using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;

namespace AmneziaGeo.Server.Tests;

public class RoutingExchangeTests
{
    [Fact]
    public async Task AFileCarriesTheRoutingAndReadsBackWhole()
    {
        using var bench = new Bench();

        var rule = await bench.Rules.AddAsync(Rule("tunnel", "out", "away", ["geosite:youtube"], ["client-1"]), CancellationToken.None);
        var drop = await bench.Rules.AddAsync(Rule("drop", "block", "", ["10.0.0.0/8"], []), CancellationToken.None);
        Assert.True(rule.IsOk, rule.Message);
        Assert.True(drop.IsOk, drop.Message);
        await bench.Rules.SaveBasicAsync(new RouteBasic { Direct = ["1.1.1.1"], Block = ["2.2.2.2"] }, CancellationToken.None);
        await bench.Presets.AddAsync(Fresh("blocked"), CancellationToken.None);
        await bench.Geo.AddAsync(Source("my-ip", "https://example.com/geoip.dat"), CancellationToken.None);

        var doc = await RoutingExport.TakeAsync(bench.Rules, bench.Presets, bench.Geo, CancellationToken.None);

        Assert.Equal(RoutingFile.FormatTag, doc.Format);
        Assert.Equal(RoutingFile.CurrentVersion, doc.Version);
        Assert.Equal(["tunnel", "drop"], doc.Rules.Select(one => one.Name).ToArray());
        Assert.Equal(["1.1.1.1"], doc.Basic.Direct);
        Assert.Equal(["2.2.2.2"], doc.Basic.Block);
        Assert.Equal(["blocked"], doc.Presets.Select(one => one.Name).ToArray());
        var mine = Assert.Single(doc.Sources, one => one.Name == "my-ip");
        Assert.Equal(GeoKind.Ip, mine.Kind);
        Assert.Equal("https://example.com/geoip.dat", mine.Url);
        Assert.True(mine.IsEnabled);

        var read = RoutingFile.Read(RoutingExport.Text(doc));

        Assert.True(read.IsOk, read.Message);
        var back = read.Doc!;
        Assert.Equal(doc.Rules.Count, back.Rules.Count);
        Assert.Equal(doc.Rules[0].Targets, back.Rules[0].Targets);
        Assert.Equal(doc.Rules[0].Clients, back.Rules[0].Clients);
        Assert.Equal(doc.Presets[0].Proxy, back.Presets[0].Proxy);
        Assert.Equal(doc.Sources[0].Name, back.Sources[0].Name);
    }

    [Fact]
    public async Task APlanNamesTheNewAndTheReplaced()
    {
        using var mine = new Bench();
        using var theirs = new Bench();

        await mine.Rules.AddAsync(Rule("tunnel", "block", "", ["geosite:ads"], []), CancellationToken.None);
        await mine.Rules.SaveBasicAsync(new RouteBasic { Direct = ["1.1.1.1"] }, CancellationToken.None);
        await mine.Presets.AddAsync(Fresh("held"), CancellationToken.None);
        await theirs.Rules.AddAsync(Rule("tunnel", "direct", "", ["geosite:ads"], []), CancellationToken.None);
        await theirs.Presets.AddAsync(Fresh("held") with { Direct = ["geoip:ru"] }, CancellationToken.None);

        var doc = await RoutingExport.TakeAsync(mine.Rules, mine.Presets, mine.Geo, CancellationToken.None);
        var plan = await RoutingImport
            .PlanAsync(doc, theirs.Rules, theirs.Presets, theirs.Geo, theirs.Outbounds, theirs.Clients, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal([], plan.NewRules);
        Assert.Equal(["tunnel"], plan.ReplacedRules);
        Assert.Equal(["held"], plan.ReplacedPresets);
        Assert.True(plan.ReplacesBasic);
    }

    [Fact]
    public async Task AnImportAddsAndReplacesAndLeavesTheRestAlone()
    {
        using var mine = new Bench();
        using var theirs = new Bench();

        await mine.Rules.AddAsync(Rule("tunnel", "block", "", ["geosite:ads"], []), CancellationToken.None);
        await mine.Rules.SaveBasicAsync(new RouteBasic { Direct = ["1.1.1.1"], Block = ["2.2.2.2"] }, CancellationToken.None);
        await mine.Presets.AddAsync(Fresh("blocked"), CancellationToken.None);
        await mine.Geo.AddAsync(Source("my-ip", "https://example.com/geoip.dat"), CancellationToken.None);
        await theirs.Rules.AddAsync(Rule("tunnel", "direct", "", ["geosite:ads"], []), CancellationToken.None);
        var heldPreset = await theirs.Presets.AddAsync(Fresh("held") with { Proxy = [] }, CancellationToken.None);
        Assert.True(heldPreset.IsOk, heldPreset.Message);
        var heldSource = await theirs.Geo.AddAsync(Source("my-site", "https://example.org/geosite.dat"), CancellationToken.None);
        Assert.True(heldSource.IsOk, heldSource.Message);

        var doc = await RoutingExport.TakeAsync(mine.Rules, mine.Presets, mine.Geo, CancellationToken.None);
        var plan = await RoutingImport
            .PlanAsync(doc, theirs.Rules, theirs.Presets, theirs.Geo, theirs.Outbounds, theirs.Clients, CancellationToken.None)
            .ConfigureAwait(false);
        var answer = await RoutingImport
            .ApplyAsync(plan, theirs.Rules, theirs.Presets, theirs.Geo, CancellationToken.None)
            .ConfigureAwait(false);

        var rules = await theirs.Rules.ListAsync(CancellationToken.None);
        var tunnel = Assert.Single(rules, one => one.Name == "tunnel");
        Assert.Equal("block", tunnel.Action);

        var presets = await theirs.Presets.ListAsync(CancellationToken.None);
        Assert.Equal(["blocked", "held"], presets.Select(one => one.Name).OrderBy(name => name).ToArray());
        var blocked = presets.Single(one => one.Name == "blocked");
        Assert.Equal(["geosite:youtube", "cidr:10.0.0.0/8"], blocked.Proxy);
        var held = presets.Single(one => one.Name == "held");
        Assert.Equal([], held.Proxy);

        var sources = await theirs.Geo.ListAsync(CancellationToken.None);
        Assert.Contains(sources, one => one.Name == "my-ip");
        Assert.Contains(sources, one => one.Name == "my-site");

        var basic = await theirs.Rules.ReadBasicAsync(CancellationToken.None);
        Assert.Equal(["1.1.1.1"], basic.Direct);
        Assert.Equal(["2.2.2.2"], basic.Block);

        Assert.Contains("rule 'tunnel'", answer.Replaced);
        Assert.Contains("preset 'blocked'", answer.Added);
        Assert.Contains("source 'my-ip'", answer.Added);
        Assert.True(answer.BasicReplaced);
        Assert.Equal([], answer.Refused);
    }

    [Fact]
    public async Task APlanNamesTheLinksTheOtherServerDoesNotHold()
    {
        using var mine = new Bench();
        using var theirs = new Bench();

        await mine.Rules.AddAsync(Rule("tunnel", "out", "away", ["geosite:youtube"], ["client-1"]), CancellationToken.None);
        await mine.Rules.AddAsync(Rule("straight", "direct", "", ["geosite:ads"], []), CancellationToken.None);

        var doc = await RoutingExport.TakeAsync(mine.Rules, mine.Presets, mine.Geo, CancellationToken.None);
        var plan = await RoutingImport
            .PlanAsync(doc, theirs.Rules, theirs.Presets, theirs.Geo, theirs.Outbounds, theirs.Clients, CancellationToken.None)
            .ConfigureAwait(false);

        Assert.Equal(["away"], plan.MissingOutbounds);
        Assert.Equal(["client-1"], plan.MissingClients);
        Assert.Equal(["rule 'tunnel': the outbound 'away' and the clients 'client-1' are not on this server"], plan.Detached);
        Assert.Equal([], plan.Detached.Where(one => one.Contains("'straight'")).ToArray());
    }

    [Fact]
    public async Task AFileOfAnotherKindOrNewerSchemaIsRefused()
    {
        var empty = RoutingFile.Read("");

        Assert.False(empty.IsOk);
        Assert.Equal("routing-empty", empty.Code);

        var kind = RoutingFile.Read("""{"format":"other","version":1}""");

        Assert.False(kind.IsOk);
        Assert.Equal("routing-kind", kind.Code);

        var newer = RoutingFile.Read($$"""{"format":"{{RoutingFile.FormatTag}}","version":{{RoutingFile.CurrentVersion + 1}}}""");

        Assert.False(newer.IsOk);
        Assert.Equal("routing-newer", newer.Code);

        var broken = RoutingFile.Read("{not json");

        Assert.False(broken.IsOk);
        Assert.Equal("routing-broken", broken.Code);
    }

    private static RouteRule Rule(string name, string action, string outbound, IReadOnlyList<string> targets, IReadOnlyList<string> clients) => new()
    {
        Name = name,
        IsEnabled = true,
        Action = action,
        Outbound = outbound,
        Targets = targets,
        Clients = clients,
    };

    private static RoutingPreset Fresh(string name) => new()
    {
        Name = name,
        Proxy = ["geosite:youtube", "10.0.0.0/8"],
    };

    private static GeoSource Source(string name, string url) => new()
    {
        Name = name,
        Kind = GeoKind.Ip,
        Url = url,
        Position = 1,
        IsEnabled = true,
    };
}
