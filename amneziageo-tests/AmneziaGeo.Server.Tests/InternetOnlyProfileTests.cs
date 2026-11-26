using System.Net;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Dal.Exchange;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Exchange;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public sealed class InternetOnlyProfileTests
{
    [Fact]
    public async Task TheProfileAddsOneScopedBlockAndKeepsTheExceptionAndDefaultDns()
    {
        using var bench = new Bench();
        var first = new ConfigEntity { Name = "awg0" };
        var second = new ConfigEntity { Name = "awg1" };
        bench.Db.Configs.AddRange(first, second);
        var dns = new TrafficRuleEntity { Name = "Default DNS", DefaultKey = "resolver-default", Dns = "9.9.9.9" };
        bench.Db.Rules.Add(new RoutingEntryEntity
        {
            Name = dns.Name,
            Position = int.MaxValue,
            Action = RouteAction.Direct,
            IsEnabled = true,
            Protocol = RouteProtocol.Any,
            Matches = [new RoutingEntryRuleEntity { Rule = dns }],
        });
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        var before = await bench.Rules.AddAsync(RouteDefaults.Fresh("Internal DNS") with
        {
            Action = RouteAction.Direct,
            Targets = ["192.168.1.53/32"],
            Ports = ["53"],
            Inbounds = [first.Name],
        }, default).ConfigureAwait(false);
        Assert.True(before.IsOk);

        var added = await bench.Rules.AddInternetOnlyAsync([first.Id, first.Id], "Internet only", "Private networks", default).ConfigureAwait(false);
        Assert.True(added!.IsOk);
        var record = added.Record!;
        Assert.Equal([first.Name], record.Inbounds);
        Assert.Equal(RouteAction.Block, record.Action);
        Assert.Single(record.RuleIds);
        Assert.Null(await bench.Rules.AddInternetOnlyAsync([first.Id], "Internet only", "Private networks", default).ConfigureAwait(false));

        var rows = await bench.Rules.TableAsync(default).ConfigureAwait(false);
        Assert.Equal(["Internal DNS", "Internet only", "Default DNS"], rows.Select(row => row.Name));
        var settings = DnsDefaults.Settings with { IsEnabled = true };
        var plan = RoutePlan.Build(rows, [], GeoIndex.Load([], new MemoryGeoFiles()), [first.Name, second.Name], dns: settings);
        Assert.Equal(["9.9.9.9"], DnsRouting.Select(plan, "example.test", inbound: first.Name).Servers);
        Assert.Equal(RouteProbe.Block, Test("192.168.1.53", first.Name, 443).Verdict);
        Assert.Equal(RouteProbe.Block, Test("fd00::1", first.Name, 443).Verdict);
        Assert.Equal(RouteProbe.Host, Test("1.1.1.1", first.Name, 443).Verdict);
        Assert.Equal(RouteProbe.Host, Test("192.168.1.53", second.Name, 443).Verdict);
        Assert.Equal(RouteProbe.Resolver, Test("192.168.1.53", first.Name, 53).Verdict);

        var ruleset = RouteRuleset.Text(plan);
        var allowDns = $"@{RouteRuleset.RangeSet(record.Id, false)} meta l4proto {{ tcp, udp }} th dport 53 return";
        Assert.Contains(allowDns, ruleset);
        Assert.DoesNotContain(allowDns, RouteRuleset.Text(plan with { Unanswered = [first.Name] }));
        Assert.DoesNotContain(allowDns, RouteRuleset.Text(plan with { Dns = settings with { IsEnabled = false } }));

        var more = await bench.Rules.AddInternetOnlyAsync([first.Id, second.Id], "Internet only", "Private networks", default).ConfigureAwait(false);
        Assert.True(more!.IsOk);
        Assert.Equal([second.Name], more.Record!.Inbounds);
        Assert.Equal(record.RuleIds, more.Record.RuleIds);
        Assert.True((await bench.Rules.SwitchAsync(record.Id, false, default).ConfigureAwait(false)).IsOk);
        Assert.Null(await bench.Rules.AddInternetOnlyAsync([first.Id, second.Id], "Internet only", "Private networks", default).ConfigureAwait(false));
        Assert.False((await bench.Rules.FindAsync(record.Id, default).ConfigureAwait(false))!.IsEnabled);

        RouteVerdict Test(string address, string inbound, int port) =>
            RouteProbe.Test(plan, new RouteQuery { Addresses = [IPAddress.Parse(address)], Inbound = inbound, Port = port, Protocol = RouteProtocol.Udp },
                (_, _) => false);
    }

    [Fact]
    public async Task ExportAndImportKeepTheProfileIdentity()
    {
        using var source = new Bench();
        using var target = new Bench();
        var first = new ConfigEntity { Name = "awg0" };
        var second = new ConfigEntity { Name = first.Name };
        source.Db.Configs.Add(first);
        target.Db.Configs.Add(second);
        await source.Db.SaveChangesAsync().ConfigureAwait(false);
        await target.Db.SaveChangesAsync().ConfigureAwait(false);

        var made = await source.Rules.AddInternetOnlyAsync([first.Id], "Internet only", "Private networks", default).ConfigureAwait(false);
        Assert.True(made!.IsOk);
        var document = await RoutingExport.TakeAsync(source.Rules, source.Presets, source.Geo, default).ConfigureAwait(false);
        var parsed = RoutingFile.Read(RoutingExport.Text(document));
        Assert.True(parsed.IsOk, parsed.Message);
        var plan = await RoutingImport.PlanAsync(parsed.Doc!, target.Rules, target.Presets, target.Geo, target.Outbounds, target.Clients, default).ConfigureAwait(false);
        var answer = await RoutingImport.ApplyAsync(plan, target.Rules, target.Presets, target.Geo, default).ConfigureAwait(false);
        Assert.Empty(answer.Refused);
        Assert.Equal(RouteDefaults.InternetOnly, Assert.Single(await target.Rules.ListCriteriaAsync(default).ConfigureAwait(false)).DefaultKey);
        Assert.Null(await target.Rules.AddInternetOnlyAsync([second.Id], "Internet only", "Private networks", default).ConfigureAwait(false));
    }
}
