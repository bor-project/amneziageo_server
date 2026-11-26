using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Template;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class DefaultTemplateTests
{
    [Fact]
    public async Task FreshInstallHasOnlyTheDefaultAndDoesNotRecreateCustomTemplates()
    {
        using var bench = new Bench();
        Assert.Empty(await bench.Templates.ListAsync(default).ConfigureAwait(false));
        Assert.True((await bench.Templates.DefaultAsync(default).ConfigureAwait(false)).IsDefault);
        Assert.Null(await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false));
        var made = (await bench.Templates.AddAsync(new ClientTemplate { Name = "mine" }, default).ConfigureAwait(false)).Record!;
        await bench.Templates.RemoveAsync(made.Id, default).ConfigureAwait(false);
        await bench.Templates.SeedAsync(default).ConfigureAwait(false);
        Assert.Empty(await bench.Templates.ListAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task TheDefaultRetainsAllTemplateSettingsAcrossScopes()
    {
        using var bench = new Bench();
        var proxies = new TransportProxyStore(bench.Db);
        var proxy = await proxies.SaveAsync(0, new TransportProxyEntity
        {
            Name = "secure", Hosted = false, Host = "proxy.example.net", Port = 443, Path = "front",
        }, default).ConfigureAwait(false);
        var preset = (await bench.Presets.AddAsync(new RoutingPreset { Name = "routes" }, default).ConfigureAwait(false)).Record!;
        var limits = new TrafficLimits(true, 1_000_000, 2_000_000);
        var draft = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        var saved = await bench.Templates.ChangeDefaultAsync(draft with
        {
            Entries = ["192.0.2.0/24"], AllowedIps = ["192.0.2.0/24"], DnsMode = DnsChoice.Own,
            Dns = ["9.9.9.9"], Mtu = 1280, Keepalive = 10, StandIns = true,
            Presets = [preset.Id], PresetsOnly = true, ProxyIds = [proxy.Id], Limits = limits,
        }, null, default).ConfigureAwait(false);
        Assert.True(saved.IsOk, saved.Message);
        bench.Db.ChangeTracker.Clear();
        var read = await new TemplateStore(bench.Db).DefaultAsync(default).ConfigureAwait(false);
        Assert.Equal(0, read.Id);
        Assert.True(read.IsDefault);
        Assert.Equal(["9.9.9.9"], read.Dns);
        Assert.Equal(DnsChoice.Own, read.DnsMode);
        Assert.Equal(1280, read.Mtu);
        Assert.Equal(10, read.Keepalive);
        Assert.True(read.StandIns);
        Assert.True(read.PresetsOnly);
        Assert.Equal([preset.Id], read.Presets);
        Assert.Equal([proxy.Id], read.ProxyIds);
        Assert.Equal(limits, read.Limits);
        Assert.Equal(["cidr:192.0.2.0/24"], read.Entries);
    }

    [Fact]
    public async Task ResetPreservesKeysClientsAndCustomTemplatesAndAllowsEditingAgain()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var custom = (await bench.Templates.AddAsync(new ClientTemplate { Name = "default", Mtu = 1320 }, default).ConfigureAwait(false)).Record!;
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { Mtu = 1280 }, [client.Id], default).ConfigureAwait(false)).IsOk);
        await bench.Templates.ResetDefaultAsync(default).ConfigureAwait(false);
        Assert.Null(await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false));
        Assert.True(System.Text.Json.JsonSerializer.Serialize(client) == System.Text.Json.JsonSerializer.Serialize(await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false)), "client changed");
        Assert.True(System.Text.Json.JsonSerializer.Serialize(endpoint) == System.Text.Json.JsonSerializer.Serialize(await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false)), "interface changed");
        Assert.Equivalent(custom, await bench.Templates.FindAsync(custom.Id, default).ConfigureAwait(false));
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { Mtu = 1360 }, null, default).ConfigureAwait(false)).IsOk);
        Assert.Equal(1360, (await bench.Templates.DefaultAsync(default).ConfigureAwait(false)).Mtu);
    }

    [Fact]
    public async Task ANameChangeOrInvalidSelectionDoesNotWriteTheDefault()
    {
        using var bench = new Bench();
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.Equal("default-name-fixed", (await bench.Templates.ChangeDefaultAsync(start with { Name = "changed" }, null, default).ConfigureAwait(false)).Code);
        Assert.Equal("unknown-proxy", (await bench.Templates.ChangeDefaultAsync(start with { ProxyIds = [999] }, null, default).ConfigureAwait(false)).Code);
        Assert.Equal("unknown-client", (await bench.Templates.ChangeDefaultAsync(start, [999], default).ConfigureAwait(false)).Code);
        Assert.Null(await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false));
    }

    [Fact]
    public async Task ProxyAuthorizationAndLimitsFollowTheDefaultOnlyForUnassignedClients()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var plain = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "plain"), default).ConfigureAwait(false)).Record!;
        var custom = (await bench.Templates.AddAsync(new ClientTemplate { Name = "custom" }, default).ConfigureAwait(false)).Record!;
        var named = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "named") with { TemplateId = custom.Id }, default).ConfigureAwait(false)).Record!;
        var proxies = new TransportProxyStore(bench.Db);
        var proxy = await proxies.SaveAsync(0, new TransportProxyEntity { Name = "front", Hosted = false, Host = "proxy.example", Port = 443, Path = "front" }, default).ConfigureAwait(false);
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        var limits = new TrafficLimits(true, 1_000_000, 2_000_000);
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { ProxyIds = [proxy.Id], Limits = limits }, null, default).ConfigureAwait(false)).IsOk);
        var credentials = await proxies.CredentialsAsync(proxy, default).ConfigureAwait(false);
        Assert.Contains(TransportProxyStore.Credential(proxy, plain.Id), credentials);
        Assert.DoesNotContain(TransportProxyStore.Credential(proxy, named.Id), credentials);
        var effective = await bench.Templates.EffectiveListAsync(default).ConfigureAwait(false);
        var ledger = new TrafficLedger(bench.Clock);
        ledger.SetLimits([plain, named], effective);
        Assert.NotNull(ledger.Of(plain).Limits);
        Assert.Null(ledger.Of(named).Limits);
        await bench.Templates.ResetDefaultAsync(default).ConfigureAwait(false);
        Assert.DoesNotContain(TransportProxyStore.Credential(proxy, plain.Id), await proxies.CredentialsAsync(proxy, default).ConfigureAwait(false));
    }

    [Fact]
    public async Task ProxyRemovalAndPresetUsageIncludeTheDefault()
    {
        using var bench = new Bench();
        var proxies = new TransportProxyStore(bench.Db);
        var proxy = await proxies.SaveAsync(0, new TransportProxyEntity { Name = "front", Hosted = false, Host = "proxy.example", Port = 443, Path = "front" }, default).ConfigureAwait(false);
        var preset = (await bench.Presets.AddAsync(new RoutingPreset { Name = "routes" }, default).ConfigureAwait(false)).Record!;
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { ProxyIds = [proxy.Id], Presets = [preset.Id] }, null, default).ConfigureAwait(false)).IsOk);
        Assert.Equal("preset-in-use", (await bench.Presets.RemoveAsync(preset.Id, default).ConfigureAwait(false)).Code);
        await proxies.RemoveAsync(proxy.Id, default).ConfigureAwait(false);
        Assert.Empty((await bench.Templates.DefaultAsync(default).ConfigureAwait(false)).ProxyIds);
    }

    [Fact]
    public async Task AnUpgradePreservesExistingTemplatesAndInheritedConfiguration()
    {
        using var bench = new Bench();
        var migrator = bench.Db.GetService<IMigrator>();
        var custom = (await bench.Templates.AddAsync(new ClientTemplate { Name = "default", Mtu = 1300 }, default).ConfigureAwait(false)).Record!;
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var before = ClientText.Text(endpoint, client);
        await migrator.MigrateAsync("20261122135207_ProxyNameTemplate").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        await migrator.MigrateAsync().ConfigureAwait(false);
        await bench.Templates.SeedAsync(default).ConfigureAwait(false);
        Assert.Equivalent(custom, await bench.Templates.FindAsync(custom.Id, default).ConfigureAwait(false));
        Assert.Equal(before, ClientText.Text(endpoint, client, await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false)));
        var start = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.True((await bench.Templates.ChangeDefaultAsync(start with { ProxyIds = [] }, null, default).ConfigureAwait(false)).IsOk);
        Assert.Equal(before, ClientText.Text(endpoint, client, await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false)));
        var empty = endpoint with { AllowedIps = [], Dns = [] };
        Assert.Equal(ClientText.Text(empty, client), ClientText.Text(empty, client,
            await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false)));
    }
}
