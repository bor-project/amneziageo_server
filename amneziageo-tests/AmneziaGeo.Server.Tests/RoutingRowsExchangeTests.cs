using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class RoutingRowsExchangeTests
{
    [Fact]
    public async Task ExportAndImportReplaceTheDefaultByIdentityWithoutCreatingADuplicate()
    {
        using var source = new Bench();
        using var target = new Bench();
        var sourceDefault = await AddDefaultAsync(source, "source default", "source DNS", RouteAction.Out, "direct",
            "9.9.9.9, 149.112.112.112", """{"direct":["208.67.222.222","208.67.220.220"]}""").ConfigureAwait(false);
        var targetDefault = await AddDefaultAsync(target, "target default", "target DNS", RouteAction.Direct, "",
            "1.1.1.1", "{}").ConfigureAwait(false);

        var exported = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, default).ConfigureAwait(false);
        var defaultRow = Assert.Single(exported.Entries, one => one.IsDefault);
        Assert.Equal(sourceDefault.Name, defaultRow.Name);
        var read = RoutingFile.Read(RoutingExport.Text(exported));
        Assert.True(read.IsOk, read.Message);

        var plan = await PlanAsync(read.Doc!, target).ConfigureAwait(false);
        Assert.Equal([targetDefault.Name], plan.ReplacedEntries);
        Assert.Empty(plan.NewEntries);
        var applied = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, default).ConfigureAwait(false);
        Assert.Empty(applied.Refused);

        var rows = await target.Rules.TableAsync(default).ConfigureAwait(false);
        var after = Assert.Single(rows);
        Assert.Equal(targetDefault.Id, after.Id);
        Assert.Equal(targetDefault.Name, after.Name);
        Assert.Equal(targetDefault.Position, after.Position);
        Assert.True(after.IsDefault);
        Assert.Equal(RouteAction.Out, after.Action);
        Assert.Equal("direct", after.Outbound);
        var criterion = Assert.Single(after.Criteria);
        Assert.Equal(TrafficDnsMode.Custom, criterion.DnsMode);
        Assert.Equal(["9.9.9.9", "149.112.112.112"], criterion.Dns);
        Assert.Equal(["208.67.222.222", "208.67.220.220"], criterion.DnsMembers["direct"]);
        Assert.DoesNotContain(await target.Rules.ListCriteriaAsync(default).ConfigureAwait(false), one => one.Name == "source DNS");
    }

    [Fact]
    public async Task OldFileKeepsTheLocalDefaultAndSplitsItsCriteriaInPlace()
    {
        using var bench = new Bench();
        var fallback = await AddDefaultAsync(bench, "local default", "local DNS", RouteAction.Out, "direct",
            "1.1.1.1", "{}").ConfigureAwait(false);
        var existing = await bench.Rules.AddAsync(new RouteRule
        {
            Name = "first",
            Action = RouteAction.Direct,
            Targets = ["unchanged.example"],
        }, default).ConfigureAwait(false);
        Assert.True(existing.IsOk, existing.Message);

        var read = RoutingFile.Read("""
            {
              "format":"amneziageo-routing","version":4,
              "rules":[
                {"name":"first targets","targets":["overlap.example"],"dns":[],"dnsMode":"default"},
                {"name":"second targets","targets":["second.example","overlap.example"],"dns":["9.9.9.9","149.112.112.112"],
                 "dnsMode":"custom","dnsMembers":{"pool":["8.8.8.8","8.8.4.4"]}},
                {"name":"local DNS","targets":["last.example"],"dns":["1.0.0.1"],"dnsMode":"custom"}
              ],
              "entries":[
                {"name":"local default","position":2,"isEnabled":true,"action":"direct","outbound":"","rules":["local DNS"],
                 "clients":[],"inbounds":[],"ports":[],"sourcePorts":[],"protocol":"any"},
                {"name":"first","position":1,"isEnabled":false,"action":"out","outbound":"pool",
                 "rules":["first targets","second targets"],"clients":["peer"],"inbounds":["wg0"],"ports":["443"],"sourcePorts":["53"],"protocol":"tcp"}
              ]
            }
            """);
        Assert.True(read.IsOk, read.Message);
        for (var pass = 0; pass < 2; pass++)
        {
            var plan = await PlanAsync(read.Doc!, bench).ConfigureAwait(false);
            Assert.DoesNotContain(fallback.Name, plan.ReplacedEntries);
            var answer = await RoutingImport.ApplyAsync(plan, bench.Rules, bench.Presets, bench.Geo, default).ConfigureAwait(false);
            Assert.Empty(answer.Refused);
        }

        var rows = await bench.Rules.TableAsync(default).ConfigureAwait(false);
        Assert.Collection(rows, first =>
        {
            Assert.Equal(existing.Record!.Id, first.Id);
            Assert.Equal("first", first.Name);
            Assert.False(first.IsEnabled);
            Assert.Equal(["peer"], first.Clients);
            Assert.Equal(["wg0"], first.Inbounds);
            Assert.Equal(["443"], first.Ports);
            Assert.Equal(["53"], first.SourcePorts);
            Assert.Equal(RouteProtocol.Tcp, first.Protocol);
            Assert.Equal(TrafficDnsMode.Default, Assert.Single(first.Criteria).DnsMode);
        }, second =>
        {
            Assert.False(second.IsEnabled);
            Assert.Equal("pool", second.Outbound);
            Assert.Equal(["peer"], second.Clients);
            var criterion = Assert.Single(second.Criteria);
            Assert.Equal(TrafficDnsMode.Custom, criterion.DnsMode);
            Assert.Equal(["9.9.9.9", "149.112.112.112"], criterion.Dns);
            Assert.Equal(["8.8.8.8", "8.8.4.4"], criterion.DnsMembers["pool"]);
        }, legacy =>
        {
            Assert.False(legacy.IsDefault);
            Assert.Equal(["last.example"], legacy.Targets);
            Assert.Equal(["1.0.0.1"], Assert.Single(legacy.Criteria).Dns);
        }, last =>
        {
            Assert.Equal(fallback.Id, last.Id);
            Assert.True(last.IsDefault);
            Assert.Equal(RouteAction.Out, last.Action);
            Assert.Equal(["1.1.1.1"], Assert.Single(last.Criteria).Dns);
        });
    }

    [Fact]
    public async Task AFileCannotActivateADuplicateCriterionBySplittingTheRow()
    {
        using var bench = new Bench();
        var criterion = new TrafficRuleBlock("targets", ["test.example"], []);
        var row = new EntryBlock("duplicate", 1, true, RouteAction.Direct, "", [criterion.Name, criterion.Name],
            [], [], [], [], RouteProtocol.Any);
        var doc = new RoutingDoc(RoutingFile.FormatTag, RoutingFile.CurrentVersion, [criterion], [row], [], []);

        var plan = await PlanAsync(doc, bench).ConfigureAwait(false);
        Assert.Equal(row.Rules, Assert.Single(plan.Doc.Entries).Rules);
        var answer = await RoutingImport.ApplyAsync(plan, bench.Rules, bench.Presets, bench.Geo, default).ConfigureAwait(false);
        Assert.Single(answer.Refused);
        Assert.Empty(await bench.Rules.TableAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public void ADefaultWithTargetsOrMoreThanOnePolicyIsRefused()
    {
        var rule = new TrafficRuleBlock("default DNS", [], []);
        var entry = new EntryBlock("default", 1, true, RouteAction.Direct, "", [rule.Name],
            [], [], [], [], RouteProtocol.Any, IsDefault: true);
        var doc = new RoutingDoc(RoutingFile.FormatTag, RoutingFile.CurrentVersion, [rule], [entry], [], []);

        Assert.True(RoutingFile.Read(RoutingExport.Text(doc)).IsOk);
        var invalid = new[]
        {
            doc with { Rules = [rule with { Targets = ["192.0.2.0/24"] }] },
            doc with { Entries = [entry with { Ports = ["53"] }] },
            doc with { Entries = [entry, entry with { Name = "other default", Position = 2 }] },
            doc with { Entries = [entry, entry with { Name = "after default", Position = 2, IsDefault = false }] },
        };
        Assert.All(invalid, one => Assert.Equal("routing-broken", RoutingFile.Read(RoutingExport.Text(one)).Code));
    }

    private static Task<RoutingPlan> PlanAsync(RoutingDoc doc, Bench bench) =>
        RoutingImport.PlanAsync(doc, bench.Rules, bench.Presets, bench.Geo, bench.Outbounds, bench.Clients, default);

    private static async Task<RoutingEntryEntity> AddDefaultAsync(
        Bench bench, string name, string criterionName, string action, string outbound, string dns, string dnsMembers)
    {
        var today = bench.Clock.GetUtcNow();
        var criterion = new TrafficRuleEntity
        {
            Name = criterionName,
            DefaultKey = RouteDefaults.FallbackKey,
            DnsMode = TrafficDnsMode.Custom,
            Dns = dns,
            DnsMembers = dnsMembers,
            CreatedUtc = today,
            UpdatedUtc = today,
        };
        var entry = new RoutingEntryEntity
        {
            Name = name,
            Position = int.MaxValue,
            Action = action,
            Outbound = outbound,
            IsEnabled = true,
            Protocol = RouteProtocol.Any,
            Matches = [new RoutingEntryRuleEntity { Rule = criterion }],
            CreatedUtc = today,
            UpdatedUtc = today,
        };
        bench.Db.Rules.Add(entry);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        return entry;
    }
}
