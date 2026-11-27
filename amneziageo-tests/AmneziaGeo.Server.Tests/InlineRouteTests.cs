using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Geo;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public sealed class InlineRouteTests
{
    [Fact]
    public async Task ChangingAnInlineCriterionKeepsOtherUsersOfTheLegacyCriterion()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;
        var rule = new RouteRule { Name = "first", Action = RouteAction.Direct };
        var criterion = new TrafficRule { Name = rule.Name, Targets = ["office.example"], Dns = ["9.9.9.9"] };
        var first = await bench.Rules.AddAsync(rule, ct, criterion);
        Assert.True(first.IsOk, first.Message);
        var shared = Assert.Single(first.Record!.RuleIds);
        var second = await bench.Rules.AddAsync(rule with { Name = "second", RuleIds = [shared] }, ct);
        Assert.True(second.IsOk, second.Message);

        var changed = await bench.Rules.ChangeAsync(first.Record.Id, rule, ct,
            criterion with { Targets = ["other.example"], Dns = ["1.1.1.1"] });
        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(first.Record.Id, changed.Record!.Id);
        Assert.Equal(first.Record.Position, changed.Record.Position);
        Assert.NotEqual(shared, Assert.Single(changed.Record.RuleIds));
        Assert.Equal("other.example", Assert.Single(changed.Record.Targets));

        var read = await bench.Rules.FindAsync(second.Record!.Id, ct);
        Assert.Equal(shared, Assert.Single(read!.RuleIds));
        Assert.Equal("office.example", Assert.Single(read.Targets));
        Assert.Equal("9.9.9.9", Assert.Single(Assert.Single(read.Criteria).Dns));
    }

    [Fact]
    public async Task TheFallbackStaysLastAndChangesDnsInPlace()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;
        var fallback = new RoutingEntryEntity
        {
            Name = "Default DNS",
            Position = int.MaxValue,
            Action = RouteAction.Direct,
            Protocol = RouteProtocol.Any,
            IsEnabled = true,
            Matches = [new RoutingEntryRuleEntity
            {
                Rule = new TrafficRuleEntity { Name = "Default DNS", DefaultKey = RouteDefaults.FallbackKey, DnsMode = TrafficDnsMode.Default },
            }],
        };
        bench.Db.Rules.Add(fallback);
        await bench.Db.SaveChangesAsync(ct);
        bench.Db.ChangeTracker.Clear();

        var ordinary = new RouteRule { Name = "first", Action = RouteAction.Direct };
        var created = await bench.Rules.AddAsync(ordinary, ct, new TrafficRule { Name = ordinary.Name, Targets = ["example.com"] });
        Assert.True(created.IsOk, created.Message);
        Assert.Equal("fixed-default-route", (await bench.Rules.SwitchAsync(fallback.Id, false, ct)).Code);
        Assert.Equal("fixed-default-route", (await bench.Rules.RemoveAsync(fallback.Id, ct)).Code);
        Assert.Equal("fixed-default-route", (await bench.Rules.PlaceAsync(fallback.Id, 1, ct, table: true)).Code);
        Assert.True((await bench.Rules.MoveAsync(created.Record!.Id, false, ct, table: true)).IsOk);
        Assert.Equal("use-default-route", (await bench.Rules.AddAsync(ordinary with { Name = "all" }, ct, new TrafficRule { Name = "all" })).Code);

        var original = await bench.Rules.FindAsync(fallback.Id, ct);
        var policy = Assert.Single(original!.Criteria);
        var adjusted = await bench.Rules.ChangeAsync(fallback.Id, original, ct, policy with { DnsMode = TrafficDnsMode.Custom, Dns = ["9.9.9.9"] });
        Assert.True(adjusted.IsOk, adjusted.Message);
        Assert.True(adjusted.Record!.IsDefault);
        Assert.Equal(policy.Id, Assert.Single(adjusted.Record.Criteria).Id);
        Assert.Equal("9.9.9.9", Assert.Single(Assert.Single(adjusted.Record.Criteria).Dns));
        Assert.Equal(fallback.Id, (await bench.Rules.TableAsync(ct))[^1].Id);
    }

    [Fact]
    public async Task AnExistingOrdinaryCatchAllKeepsItsScopeWhenDnsIsEdited()
    {
        using var bench = new Bench();
        var ct = CancellationToken.None;
        var legacy = new RouteRule { Name = "all clients", Action = RouteAction.Direct };
        var added = await bench.Rules.AddAsync(legacy, ct);
        Assert.True(added.IsOk, added.Message);
        var policy = Assert.Single(added.Record!.Criteria);
        var edited = await bench.Rules.ChangeAsync(added.Record.Id, legacy, ct, policy with { Dns = ["9.9.9.9"] });
        Assert.True(edited.IsOk, edited.Message);
        Assert.False(edited.Record!.HasTargets);
        Assert.Equal("9.9.9.9", Assert.Single(Assert.Single(edited.Record.Criteria).Dns));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidNestedDnsListsAreRefused(bool missingList)
    {
        var members = new Dictionary<string, IReadOnlyList<string>>
        {
            ["direct"] = missingList ? null! : [null!],
        };
        var criterion = new RouteCriterionRequest(["example.com"], TrafficDnsMode.Custom, ["9.9.9.9"], members);
        var request = new RouteRequest("test", true, RouteAction.Direct, "", null, [], RouteProtocol.Any, Criterion: criterion);

        Assert.Equal("bad-criterion", RouteAnswers.Dropped(request)?.Code);
    }

    [Fact]
    public void PortSpecificBlockCanYieldDnsWithoutYieldingTraffic()
    {
        var blocked = new RouteRule
        {
            Id = 1, Name = "udp block", Position = 1, Action = RouteAction.Block,
            Protocol = RouteProtocol.Udp, Ports = ["443"],
            Criteria = [new TrafficRule { Id = 1, Name = "block", DnsMode = TrafficDnsMode.Skip }],
        };
        var fallback = new RouteRule
        {
            Id = 2, Name = "fallback", Position = int.MaxValue, Action = RouteAction.Direct,
            Criteria = [new TrafficRule { Id = 2, Name = "fallback", DefaultKey = RouteDefaults.FallbackKey, Dns = ["9.9.9.9"] }],
        };

        var plan = RoutePlan.Build([blocked, fallback], [], GeoIndex.Load([], new MemoryGeoFiles()), ["awg1"]);
        Assert.Equal(["9.9.9.9"], DnsRouting.Select(plan, "example.com", inbound: "awg1").Servers);
        Assert.True(plan.Legs[0].IsBlock);
        var text = RouteRuleset.Text(plan);
        Assert.Contains("udp dport { 443 }", text, StringComparison.Ordinal);
        Assert.True(plan.Legs[^1].Rule.IsDefault);
    }
}
