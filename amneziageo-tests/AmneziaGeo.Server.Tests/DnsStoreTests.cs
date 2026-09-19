using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Tests;

public class DnsStoreTests
{
    [Fact]
    public async Task AFreshInstallCarriesTheSettingsTheServerStartsWith()
    {
        using var bench = new Bench();

        var held = await bench.Resolver.ReadAsync(CancellationToken.None);

        Assert.False(held.IsEnabled);
        Assert.Equal(DnsDefaults.Port, held.Port);
        Assert.Equal(DnsDefaults.Upstreams, held.Upstreams);
        Assert.Empty(held.Listen);
    }

    [Fact]
    public async Task SettingsAreReadBackAsTheyWereSaved()
    {
        using var bench = new Bench();
        var settings = DnsDefaults.Settings with
        {
            IsEnabled = true,
            Port = 5300,
            Upstreams = ["9.9.9.9", "1.1.1.1:5353"],
            Outbound = "bor",
            Listen = ["10.8.0.1"],
            NameMinutes = 30,
            CacheSize = 128,
            MinTtl = 30,
            MaxTtl = 600,
            Intercept = false,
            BlockDot = false,
            BlockDoh = false,
        };

        var saved = await bench.Resolver.SaveAsync(settings, CancellationToken.None);
        var held = await bench.Resolver.ReadAsync(CancellationToken.None);

        Assert.True(saved.IsOk, saved.Message);
        Assert.True(held.IsEnabled);
        Assert.Equal(5300, held.Port);
        Assert.Equal(["9.9.9.9", "1.1.1.1:5353"], held.Upstreams);
        Assert.Equal("bor", held.Outbound);
        Assert.Equal(["10.8.0.1"], held.Listen);
        Assert.Equal(30, held.NameMinutes);
        Assert.Equal(128, held.CacheSize);
        Assert.Equal(30, held.MinTtl);
        Assert.Equal(600, held.MaxTtl);
        Assert.False(held.Intercept);
        Assert.False(held.BlockDot);
        Assert.False(held.BlockDoh);
    }

    [Fact]
    public async Task SavingTwiceLeavesOneSetOfSettings()
    {
        using var bench = new Bench();

        await bench.Resolver.SaveAsync(DnsDefaults.Settings with { Port = 5300 }, CancellationToken.None);
        await bench.Resolver.SaveAsync(DnsDefaults.Settings with { Port = 5301 }, CancellationToken.None);

        Assert.Equal(5301, (await bench.Resolver.ReadAsync(CancellationToken.None)).Port);
        Assert.Single(bench.Db.Resolver);
    }

    [Fact]
    public async Task SettingsTheRulesRefuseAreNotSaved()
    {
        using var bench = new Bench();

        var refused = await bench.Resolver.SaveAsync(DnsDefaults.Settings with { Upstreams = ["nowhere"] }, CancellationToken.None);

        Assert.False(refused.IsOk);
        Assert.Equal("bad-upstream", refused.Code);
        Assert.Empty(bench.Db.Resolver);
    }
}
