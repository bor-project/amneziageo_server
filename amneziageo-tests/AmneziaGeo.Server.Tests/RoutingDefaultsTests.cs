using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;
using AmneziaGeo.Server.Routing.Template;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class RoutingDefaultsTests
{
    [Fact]
    public void EveryBundledListIsUsableWithoutGeoDownloads()
    {
        Assert.Equal(7, RoutingBundle.Lists.Count);
        Assert.Equal(7, RoutingBundle.Lists.Select(list => list.Key).Distinct().Count());
        foreach (var list in RoutingBundle.Lists)
        {
            Assert.Null(RouteRules.Check(new TrafficRule { Name = list.Name, Targets = list.Targets }));
            Assert.Null(PresetRules.Check(new RoutingPreset { Name = list.Name, Proxy = [.. list.Targets, .. list.Applications] }));
            Assert.All(list.Targets, target => Assert.StartsWith("domain:", target));
        }
    }

    [Fact]
    public async Task DatabasePreparationDoesNotOptAnExistingInstallationIn()
    {
        using var bench = new Bench();
        await ServerDatabase.PrepareAsync(bench.Services).ConfigureAwait(false);

        Assert.Empty(await bench.Db.TrafficRules.ToListAsync().ConfigureAwait(false));
        Assert.Empty(await bench.Presets.ListAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task TheSharedBundleBuildsGroupsAndTunnelPresets()
    {
        using var bench = new Bench();
        var result = await Store(bench).RestoreAllAsync(default).ConfigureAwait(false);
        var groups = await new TrafficRuleStore(bench.Db).ListAsync(default).ConfigureAwait(false);
        var presets = await bench.Presets.ListAsync(default).ConfigureAwait(false);

        Assert.Equal(new RoutingDefaultsResult(7, 7), result);
        foreach (var list in RoutingBundle.Lists)
        {
            var criterion = Assert.Single(groups, group => group.Name == list.Name);
            Assert.Equal(list.Targets, criterion.Targets);
            Assert.Equal(TrafficDnsMode.Default, criterion.DnsMode);
            Assert.Empty(criterion.Dns);
            var preset = Assert.Single(presets, preset => preset.Name == list.Name);
            Assert.Equal(list.Targets.Concat(list.Applications), preset.Proxy);
            Assert.Empty(preset.Direct);
            Assert.Empty(preset.Block);
            Assert.False(preset.Full);
            Assert.False(preset.AllUdp);
            Assert.True(preset.IsDefault);
            Assert.True(Guid.TryParse(preset.Uid, out _));
        }
    }

    [Fact]
    public async Task RestoringTwicePreservesIdentifiersAndTimes()
    {
        using var bench = new Bench();
        var store = Store(bench);
        await store.RestoreAllAsync(default).ConfigureAwait(false);
        var before = await bench.Presets.ListAsync(default).ConfigureAwait(false);
        var groupIds = await bench.Db.TrafficRules.Select(group => group.Id).ToListAsync().ConfigureAwait(false);
        bench.Clock.Pass(TimeSpan.FromDays(1));

        Assert.Equal(new RoutingDefaultsResult(0, 0), await store.RestoreAllAsync(default).ConfigureAwait(false));
        var after = await bench.Presets.ListAsync(default).ConfigureAwait(false);
        Assert.Equal(before.Select(preset => (preset.Id, preset.Uid, preset.CreatedUtc, preset.UpdatedUtc)),
            after.Select(preset => (preset.Id, preset.Uid, preset.CreatedUtc, preset.UpdatedUtc)));
        Assert.Equal(groupIds, await bench.Db.TrafficRules.Select(group => group.Id).ToListAsync().ConfigureAwait(false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EachMenuRestoresOnlyItsOwnCollection(bool groups)
    {
        using var bench = new Bench();
        var store = Store(bench);

        var result = groups ? await store.RestoreRulesAsync(default).ConfigureAwait(false)
            : await store.RestorePresetsAsync(default).ConfigureAwait(false);

        Assert.Equal(groups ? new RoutingDefaultsResult(7, 0) : new RoutingDefaultsResult(0, 7), result);
        Assert.Equal(groups ? 7 : 0, await bench.Db.TrafficRules.CountAsync().ConfigureAwait(false));
        Assert.Equal(groups ? 0 : 7, await bench.Db.RoutingPresets.CountAsync().ConfigureAwait(false));
    }

    [Fact]
    public async Task DeletedDefaultsAreRecreatedWithoutDuplicatingTheRest()
    {
        using var bench = new Bench();
        var store = Store(bench);
        await store.RestoreAllAsync(default).ConfigureAwait(false);
        var group = await bench.Db.TrafficRules.FirstAsync(record => record.DefaultKey == "youtube").ConfigureAwait(false);
        var preset = await bench.Db.RoutingPresets.FirstAsync(record => record.DefaultKey == "discord").ConfigureAwait(false);
        bench.Db.Remove(group);
        bench.Db.Remove(preset);
        await bench.Db.SaveChangesAsync().ConfigureAwait(false);

        Assert.Equal(new RoutingDefaultsResult(1, 1), await store.RestoreAllAsync(default).ConfigureAwait(false));
        Assert.Equal(7, await bench.Db.TrafficRules.CountAsync().ConfigureAwait(false));
        Assert.Equal(7, await bench.Db.RoutingPresets.CountAsync().ConfigureAwait(false));
    }

    [Fact]
    public async Task RenamingAndEditingDefaultsKeepsTheirIdentityAndReferences()
    {
        using var bench = new Bench();
        var store = Store(bench);
        await store.RestoreAllAsync(default).ConfigureAwait(false);
        var groups = new TrafficRuleStore(bench.Db);
        var group = (await groups.ListAsync(default).ConfigureAwait(false)).First();
        await groups.ChangeAsync(group.Id, group with { Name = "My list", Targets = ["domain:example.test"], Dns = ["1.1.1.1"] }, default).ConfigureAwait(false);
        var preset = (await bench.Presets.ListAsync(default).ConfigureAwait(false)).First();
        var changed = await bench.Presets.ChangeAsync(preset.Id, preset with { Name = "My preset", Proxy = ["domain:example.test"], IsDefault = false, ReadOnly = true }, default).ConfigureAwait(false);
        var rule = await bench.Rules.AddAsync(RouteDefaults.Fresh("kept") with { Action = RouteAction.Block, RuleIds = [group.Id] }, default).ConfigureAwait(false);
        Assert.True(rule.IsOk, rule.Message);
        var template = (await bench.Templates.AddAsync(new AmneziaGeo.Server.Awg.Client.ClientTemplate { Name = "custom" }, default).ConfigureAwait(false)).Record!;
        var assigned = await bench.Templates.ChangeAsync(template.Id, template with { Presets = [preset.Id] }, default).ConfigureAwait(false);
        Assert.True(assigned.IsOk, assigned.Message);

        Assert.Equal(new RoutingDefaultsResult(0, 0), await store.RestoreAllAsync(default).ConfigureAwait(false));
        var held = (await groups.ListAsync(default).ConfigureAwait(false)).Single(record => record.Id == group.Id);
        Assert.Equal("My list", held.Name);
        Assert.Equal(["domain:example.test"], held.Targets);
        Assert.Equal(["1.1.1.1"], held.Dns);
        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(changed.Record),
            System.Text.Json.JsonSerializer.Serialize(await bench.Presets.FindAsync(preset.Id, default).ConfigureAwait(false)));
        Assert.Equal([group.Id], (await bench.Rules.TableAsync(default).ConfigureAwait(false)).Single().RuleIds);
        Assert.Equal([preset.Id], (await bench.Templates.FindAsync(template.Id, default).ConfigureAwait(false))!.Presets);
    }

    [Fact]
    public async Task SameNamedUserRecordsAreNeitherClaimedNorOverwritten()
    {
        using var bench = new Bench();
        var groups = new TrafficRuleStore(bench.Db);
        await groups.AddAsync(new TrafficRule { Name = "YouTube", Targets = ["domain:example.test"] }, default).ConfigureAwait(false);
        await groups.AddAsync(new TrafficRule { Name = "YouTube (AmneziaGeo)", Targets = ["domain:example.test"] }, default).ConfigureAwait(false);
        var user = await bench.Presets.AddAsync(new RoutingPreset { Name = "YouTube", Direct = ["domain:example.test"] }, default).ConfigureAwait(false);

        Assert.Equal(new RoutingDefaultsResult(7, 7), await Store(bench).RestoreAllAsync(default).ConfigureAwait(false));
        var held = await bench.Db.TrafficRules.SingleAsync(group => group.DefaultKey == "youtube").ConfigureAwait(false);
        Assert.Equal("YouTube (AmneziaGeo 2)", held.Name);
        Assert.Null((await bench.Db.TrafficRules.SingleAsync(group => group.Name == "YouTube").ConfigureAwait(false)).DefaultKey);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(user.Record),
            System.Text.Json.JsonSerializer.Serialize(await bench.Presets.FindAsync(user.Record!.Id, default).ConfigureAwait(false)));
        Assert.Equal(new RoutingDefaultsResult(0, 0), await Store(bench).RestoreAllAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task RestoringDoesNotActivateRulesOrAssignTemplates()
    {
        using var bench = new Bench();
        var templates = await bench.Templates.ListAsync(default).ConfigureAwait(false);
        var outbounds = await bench.Outbounds.ListAsync(default).ConfigureAwait(false);
        await Store(bench).RestoreAllAsync(default).ConfigureAwait(false);

        Assert.Empty(await bench.Rules.TableAsync(default).ConfigureAwait(false));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(templates),
            System.Text.Json.JsonSerializer.Serialize(await bench.Templates.ListAsync(default).ConfigureAwait(false)));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(outbounds),
            System.Text.Json.JsonSerializer.Serialize(await bench.Outbounds.ListAsync(default).ConfigureAwait(false)));
    }

    [Fact]
    public async Task ConcurrentRestoresAddEachRecordOnce()
    {
        using var bench = new Bench();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var scope = bench.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RoutingDefaultsStore>().RestoreAllAsync(default).ConfigureAwait(false);
        }))).ConfigureAwait(false);

        Assert.Equal(7, results.Sum(result => result.Rules));
        Assert.Equal(7, results.Sum(result => result.Presets));
        Assert.Equal(7, await bench.Db.TrafficRules.CountAsync().ConfigureAwait(false));
        Assert.Equal(7, await bench.Db.RoutingPresets.CountAsync().ConfigureAwait(false));
    }

    private static RoutingDefaultsStore Store(Bench bench) => new(bench.Db, bench.Clock);
}
