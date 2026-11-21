using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class TrafficTotalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CalendarBoundariesDoNotResetCumulativeTraffic()
    {
        var clock = new Clock(Noon);
        var ledger = new TrafficLedger(clock);
        var client = new TunnelClient { Id = 1, Name = "member" };
        ledger.Observe([client], [new(1, 0, 0)], Noon);
        ledger.Observe([client], [new(1, 100, 200)], Noon.AddSeconds(2));
        ledger.Turn(DateOnly.FromDateTime(Noon.AddMonths(1).UtcDateTime));
        Assert.Equal(300UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(default, ledger.Used(client));
        ledger.Observe([client], [new(1, 130, 240)], Noon.AddMonths(1));
        Assert.Equal(370UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(new ClientUsage(30, 40), ledger.Used(client));
    }

    [Fact]
    public void LoadedHistoryAndLiveDailyCountersAreNotCountedTwice()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        var client = new TunnelClient { Id = 1, Name = "member" };
        ledger.Load([new TrafficDay(1, day, new(100, 200), new(1000, 2000))]);
        ledger.LoadHistory([new TrafficHistory(1, new(500, 600), day.AddDays(-100), default, null)]);
        ledger.Observe([client], [new(1, 1030, 2040)], Noon);
        Assert.Equal(1170UL, ledger.Of(client).Total!.TotalBytes);
        ledger.Observe([client], [new(1, 1030, 2040)], Noon.AddSeconds(2));
        Assert.Equal(1170UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(day.AddDays(-100), ledger.Of(client).Total!.SinceDay);
    }

    [Fact]
    public void ResettingTrafficPreservesTheQuotaAndDailyUsage()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var client = new TunnelClient
        {
            Id = 1, Name = "member", OverrideLimits = true,
            Limits = new TrafficLimits(Enabled: true, QuotaBytes: 10000, QuotaAction: "block"),
        };
        ledger.SetLimits([client], []);
        ledger.Observe([client], [new(1, 0, 0)], Noon);
        ledger.Observe([client], [new(1, 100, 200)], Noon.AddSeconds(2));
        var quota = ledger.Of(client).Limits;
        ledger.AcceptTrafficReset(1, ledger.Cumulative(1), Noon.AddSeconds(3));
        Assert.Equal(0UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(quota, ledger.Of(client).Limits);
        Assert.Equal(new ClientUsage(100, 200), ledger.Used(client));
        ledger.Observe([client], [new(1, 150, 270)], Noon.AddSeconds(4));
        Assert.Equal(120UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(420UL, ledger.Of(client).Limits!.UsedBytes);
    }

    [Fact]
    public void ResettingAQuotaDoesNotResetCumulativeTraffic()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var client = new TunnelClient
        {
            Id = 1, Name = "member", OverrideLimits = true,
            Limits = new TrafficLimits(Enabled: true, QuotaBytes: 10000, QuotaAction: "block"),
        };
        ledger.SetLimits([client], []);
        ledger.Observe([client], [new(1, 0, 0)], Noon);
        ledger.Observe([client], [new(1, 100, 200)], Noon.AddSeconds(2));
        ledger.AcceptReset(ledger.ResetPeriod(client)!);
        Assert.Equal(300UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(0UL, ledger.Of(client).Limits!.UsedBytes);
    }

    [Fact]
    public void LowerPeerCountersContinueTheCumulativeTotal()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var client = new TunnelClient { Id = 1, Name = "member" };
        ledger.Observe([client], [new(1, 0, 0)], Noon);
        ledger.Observe([client], [new(1, 100, 200)], Noon.AddSeconds(2));
        ledger.Observe([client], [new(1, 7, 11)], Noon.AddSeconds(4));
        Assert.Equal(318UL, ledger.Of(client).Total!.TotalBytes);
    }

    [Fact]
    public void OverflowAndAnUnavailableBaselineDoNotWrapTheDisplayedTotal()
    {
        var history = new TrafficHistory(1, new(ulong.MaxValue, ulong.MaxValue), null, default, null);
        Assert.Equal(ulong.MaxValue, history.Add(new(1, 1), DateOnly.MinValue).Total().TotalBytes);
        Assert.Equal(0UL, (history with { Used = default, Reset = new(10, 20) }).Total().TotalBytes);
    }

    [Fact]
    public async Task HistoricalTotalsIncludeAllDaysAndRemainSeparateForEachClient()
    {
        using var bench = new Bench(now: Noon);
        var first = await MemberAsync(bench).ConfigureAwait(false);
        var second = (await bench.Clients.AddAsync(ClientDefaults.Fresh(first.ConfigId, "second") with
        {
            Address = ["10.8.0.3/32"],
        }, default).ConfigureAwait(false)).Record!;
        var store = new TrafficStore(bench.Db);
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        await store.SaveAsync([
            new(first.Id, day.AddDays(-200), new(100, 200), default),
            new(first.Id, day, new(10, 20), new(1000, 2000)),
            new(second.Id, day, new(5, 7), default),
        ], default).ConfigureAwait(false);
        var totals = await store.HistoryAsync(default).ConfigureAwait(false);
        Assert.Equal(330UL, Assert.Single(totals, one => one.ClientId == first.Id).Total().TotalBytes);
        Assert.Equal(day.AddDays(-200), Assert.Single(totals, one => one.ClientId == first.Id).SinceDay);
        Assert.Equal(12UL, Assert.Single(totals, one => one.ClientId == second.Id).Total().TotalBytes);
    }

    [Fact]
    public async Task AnUpgradePreservesHistoryAndUsesAValidZeroResetForExistingClients()
    {
        using var bench = new Bench(now: Noon);
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var store = new TrafficStore(bench.Db);
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        await store.SaveAsync([new(client.Id, day, new(100, 200), new(100, 200))], default).ConfigureAwait(false);
        var migrator = bench.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261119120000_TrafficLimits").ConfigureAwait(false);
        await migrator.MigrateAsync().ConfigureAwait(false);
        await migrator.MigrateAsync().ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        var total = Assert.Single(await store.HistoryAsync(default).ConfigureAwait(false));
        Assert.Equal(300UL, total.Total().TotalBytes);
        Assert.Null(total.ResetUtc);
        Assert.Equal(default, total.Reset);
        Assert.Single(await store.DaysAsync(client.Id, day, day, default).ConfigureAwait(false));
    }

    [Fact]
    public async Task ResetOffsetsRoundTripBeyondSignedIntegerRange()
    {
        using var bench = new Bench(now: Noon);
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var store = new TrafficStore(bench.Db);
        Assert.True(await store.ResetAsync(client.Id, new(ulong.MaxValue, ulong.MaxValue), Noon, default).ConfigureAwait(false));
        bench.Db.ChangeTracker.Clear();
        var row = Assert.Single(await store.HistoryAsync(default).ConfigureAwait(false));
        Assert.Equal(new ClientUsage(ulong.MaxValue, ulong.MaxValue), row.Reset);
        Assert.Equal(Noon, row.ResetUtc);
    }

    [Fact]
    public async Task ResetAndRestartKeepTheNewTrafficAndRetainTheQuotaAndHistory()
    {
        using var bench = new Bench(now: Noon);
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var kernel = new Kernel();
        var (ledger, meter) = Meter(bench, kernel);
        kernel.Hold(Device(client.PublicKey, 0, 0));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        kernel.Hold(Device(client.PublicKey, 100, 200));
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        Assert.Equal(300UL, ledger.Of(client).Total!.TotalBytes);
        Assert.True(await meter.ResetTrafficAsync(client.Id, default).ConfigureAwait(false));
        Assert.Equal(0UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(300UL, ledger.Of(client).Limits!.UsedBytes);
        kernel.Hold(Device(client.PublicKey, 150, 270));
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        var (restarted, next) = Meter(bench, kernel);
        await next.MeasureAsync(default).ConfigureAwait(false);
        Assert.Equal(120UL, restarted.Of(client).Total!.TotalBytes);
        Assert.Equal(420UL, restarted.Of(client).Limits!.UsedBytes);
        var store = new TrafficStore(bench.Db);
        var days = await store.DaysAsync(client.Id, DateOnly.MinValue, DateOnly.MaxValue, default).ConfigureAwait(false);
        Assert.Equal(new ClientUsage(150, 270), Assert.Single(days).Used);
        Assert.Equal(ledger.Of(client).Total!.ResetUtc, restarted.Of(client).Total!.ResetUtc);
    }

    [Fact]
    public async Task SimultaneousMeasurementsAndResetsDoNotRestoreAnOldBaseline()
    {
        using var bench = new Bench(now: Noon);
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var kernel = new Kernel();
        var (ledger, meter) = Meter(bench, kernel);
        kernel.Hold(Device(client.PublicKey, 0, 0));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        kernel.Hold(Device(client.PublicKey, 100, 200));
        await Task.WhenAll(meter.ResetTrafficAsync(client.Id, default), meter.MeasureAsync(default),
            meter.ResetTrafficAsync(client.Id, default)).ConfigureAwait(false);
        Assert.Equal(0UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Equal(300UL, ledger.Of(client).Limits!.UsedBytes);
        kernel.Hold(Device(client.PublicKey, 110, 220));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        Assert.Equal(30UL, ledger.Of(client).Total!.TotalBytes);
    }

    [Fact]
    public async Task ARefusedDatabaseResetKeepsTheOldDisplayAndCanBeRetried()
    {
        var failure = new ResetFailure();
        using var bench = new Bench(now: Noon, configure: services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(failure)));
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var kernel = new Kernel();
        var (ledger, meter) = Meter(bench, kernel);
        kernel.Hold(Device(client.PublicKey, 0, 0));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        kernel.Hold(Device(client.PublicKey, 100, 200));
        await meter.MeasureAsync(default).ConfigureAwait(false);
        failure.Armed = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => meter.ResetTrafficAsync(client.Id, default)).ConfigureAwait(false);
        Assert.Equal(300UL, ledger.Of(client).Total!.TotalBytes);
        Assert.Null(ledger.Of(client).Total!.ResetUtc);
        failure.Armed = false;
        Assert.True(await meter.ResetTrafficAsync(client.Id, default).ConfigureAwait(false));
        Assert.Equal(0UL, ledger.Of(client).Total!.TotalBytes);
    }

    [Fact]
    public async Task ResettingAnUnknownClientDoesNotChangeAnExistingClient()
    {
        using var bench = new Bench(now: Noon);
        var client = await MemberAsync(bench).ConfigureAwait(false);
        var kernel = new Kernel();
        var (ledger, meter) = Meter(bench, kernel);
        Assert.False(await meter.ResetTrafficAsync(long.MaxValue, default).ConfigureAwait(false));
        Assert.Null(ledger.Of(client).Total!.ResetUtc);
    }

    private static async Task<TunnelClient> MemberAsync(Bench bench)
    {
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default).ConfigureAwait(false)).Record!;
        return (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "member") with
        {
            Address = ["10.8.0.2/32"], OverrideLimits = true,
            Limits = new TrafficLimits(Enabled: true, QuotaBytes: 1000000, QuotaAction: "block"),
        }, default).ConfigureAwait(false)).Record!;
    }

    private static (TrafficLedger Ledger, ClientMeter Meter) Meter(Bench bench, Kernel kernel)
    {
        var ledger = new TrafficLedger(bench.Clock);
        var network = new Ledger();
        network.Links.Add("awg1");
        var host = new ClientHost(network, kernel, new InterfaceFile(new InterfaceFileOptions { KeepFile = false }), bench.Clock, ledger);
        return (ledger, new ClientMeter(bench.Scopes, kernel, host, ledger, bench.Clock, NullLogger<ClientMeter>.Instance));
    }

    private static AwgDevice Device(string key, ulong rx, ulong tx) => new()
    {
        Name = "awg1", Peers = [new AwgPeer { PublicKey = key, RxBytes = rx, TxBytes = tx }],
    };

    private sealed class ResetFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<ClientEntity>().Any(entry =>
                entry.State == EntityState.Modified && entry.Property(one => one.TrafficResetUtc).IsModified))
            {
                throw new DbUpdateException("reset refused");
            }

            return ValueTask.FromResult(result);
        }
    }
}
