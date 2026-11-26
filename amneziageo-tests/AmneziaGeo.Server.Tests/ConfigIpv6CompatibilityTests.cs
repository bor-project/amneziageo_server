using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Tests;

public sealed class ConfigIpv6CompatibilityTests
{
    [Fact]
    public void LegacyRevisionsRetainTheSameBytesWithoutAnAutomaticAddress()
    {
        var config = ConfigDefaults.Fresh("legacy851") with { Id = 15, Address = ["10.85.1.1/24", "fd85:1::1/64"] };
        Assert.Equal(LegacyRevision(config), ConfigDraftStore.Revision(config));
        Assert.NotEqual(ConfigDraftStore.Revision(config), ConfigDraftStore.Revision(config with { Ipv6AutoAddress = "fd85:1::1/64" }));
    }

    [Theory]
    [InlineData("20261124140354_PendingInterfaceChanges")]
    [InlineData("20261125220000_RoutingModel")]
    public async Task UpgradingRetainsManualAddressesCredentialsClientsAndPendingRevisions(string sourceMigration)
    {
        using var bench = new Bench();
        var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("upgrade851") with
        {
            IsEnabled = false,
            Host = "vpn.example.org",
            Address = ["10.85.1.1/24", "fd85:1::1/64"],
        }, default).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "phone851") with { Address = ["10.85.1.8/32"] }, default).ConfigureAwait(false)).Record!;
        var drafts = new ConfigDraftStore(bench.Db, bench.Clock);
        await drafts.SaveAsync(config, "legacy-payload", default).ConfigureAwait(false);
        var revision = LegacyRevision(config);
        await using var legacy = await LegacyDatabase.FromAsync(bench.Db, sourceMigration).ConfigureAwait(false);
        Assert.DoesNotContain("Ipv6AutoAddress", await legacy.Db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Configs')").ToListAsync().ConfigureAwait(false));
        await legacy.MigrateAsync().ConfigureAwait(false);
        var configs = new ConfigStore(legacy.Db, bench.Clock);
        var current = (await configs.FindAsync(config.Id, default).ConfigureAwait(false))!;
        Assert.Equal(string.Empty, current.Ipv6AutoAddress);
        Assert.Equal(LegacyJson(config), LegacyJson(current));
        Assert.Equal(revision, ConfigDraftStore.Revision(current));
        Assert.Equal(JsonSerializer.Serialize(client), JsonSerializer.Serialize(await new ClientStore(legacy.Db, bench.Clock).FindAsync(client.Id, default).ConfigureAwait(false)));
        var pending = (await new ConfigDraftStore(legacy.Db, bench.Clock).FindAsync(config.Id, default).ConfigureAwait(false))!;
        Assert.Equal(revision, pending.BaseRevision);
        Assert.Equal("legacy-payload", pending.Payload);
        Assert.False(legacy.Db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void RefreshCanExcludeOnlyTheCurrentInterfaceRoute()
    {
        var route = "fd850001000000000000000000000000 40 00000000000000000000000000000000 00 00000000000000000000000000000000 00000100 00000000 00000000 00000001 ";
        var text = route + "awg851\n" + route + "other851\n";
        Assert.Equal(2, HostRoutes.Six(text).Count);
        Assert.Single(HostRoutes.Six(text, "awg851"));
        Assert.Equal(2, HostRoutes.Six(text, "missing851").Count);
        var auto = "fd85:1::1/64";
        var ownOnly = HostRoutes.Six(route + "awg851\n", "awg851").Select(one => one.Range);
        var plan = ConfigIpv6.Refresh(["10.85.1.1/24", auto], auto, true, ownOnly);
        Assert.Equal(auto, plan.AutomaticAddress);
        Assert.Equal(["10.85.1.1/24", auto], plan.Address);
    }

    private static string LegacyRevision(ServerConfig config) => "v2:" + Convert.ToHexStringLower(SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(LegacyJson(config with { CreatedUtc = default, UpdatedUtc = default }))));

    private static string LegacyJson(ServerConfig config)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(ServerConfig))
            {
                foreach (var property in info.Properties.Where(one => one.Name == nameof(ServerConfig.Ipv6AutoAddress)).ToArray())
                {
                    info.Properties.Remove(property);
                }
            }
        });
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { TypeInfoResolver = resolver });
    }
}
