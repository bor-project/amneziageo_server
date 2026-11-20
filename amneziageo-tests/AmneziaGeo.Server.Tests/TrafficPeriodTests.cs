using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Tests;

public sealed class TrafficPeriodTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("day", "2026-11-19", "2026-11-20")]
    [InlineData("week", "2026-11-16", "2026-11-23")]
    [InlineData("month", "2026-11-01", "2026-12-01")]
    public void WindowsUseTheUtcCalendar(string kind, string start, string end)
    {
        var window = TrafficPeriods.At(kind, Noon.ToOffset(TimeSpan.FromHours(14)));
        Assert.Equal(DateTimeOffset.Parse(start + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), window.Start);
        Assert.Equal(DateTimeOffset.Parse(end + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), window.End);
        Assert.Equal(TimeSpan.Zero, window.Start.Offset);
    }

    [Fact]
    public void ClientsAndPeriodsHaveIndependentUsage()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1), Client(2) };
        meter.Observe(clients, [new(1, 0, 0), new(2, 0, 0)], Generations(), Noon);
        meter.Observe(clients, [new(1, 60, 70), new(2, 10, 20)], Generations(), Noon.AddSeconds(2));
        Assert.Equal(new ClientUsage(60, 70), meter.Of(1, "month", Noon).Used);
        Assert.Equal(new ClientUsage(10, 20), meter.Of(2, "week", Noon).Used);
        Assert.Equal(6, meter.Pending().Count);
    }

    [Fact]
    public void MissingReadingsDoNotCountTheSamePeerTwice()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1) };
        meter.Observe(clients, [new(1, 100, 100)], Generations(), Noon);
        meter.Observe(clients, [], Generations(), Noon.AddSeconds(2));
        meter.Observe(clients, [new(1, 110, 120)], Generations(), Noon.AddSeconds(4));
        Assert.Equal(new ClientUsage(10, 20), meter.Of(1, "month", Noon).Used);
    }

    [Fact]
    public void ANewCounterGenerationCountsEvenAboveTheOldBaseline()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1) };
        meter.Observe(clients, [new(1, 100, 100)], Generations(), Noon);
        meter.Observe(clients, [new(1, 110, 120)], Generations(), Noon.AddSeconds(2));
        meter.Observe(clients, [new(1, 300, 400)], new Dictionary<long, string> { [1] = "new-interface" }, Noon.AddSeconds(4));
        Assert.Equal(new ClientUsage(310, 420), meter.Of(1, "month", Noon).Used);
    }

    [Fact]
    public void AConfirmedPeerRemovalKeepsTheQuotaAndRestartsItsCounters()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1) };
        meter.Observe(clients, [new(1, 100, 100)], Generations(), Noon);
        meter.Observe(clients, [new(1, 120, 130)], Generations(), Noon.AddSeconds(2));
        meter.ForgetCounters(1);
        meter.Observe(clients, [new(1, 200, 300)], Generations(), Noon.AddSeconds(4));
        Assert.Equal(new ClientUsage(220, 330), meter.Of(1, "month", Noon).Used);
    }

    [Fact]
    public void APeriodTurnsEvenWhenItsBlockedPeerIsAbsent()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1) };
        meter.Observe(clients, [new(1, 0, 0)], Generations(), Noon);
        meter.Observe(clients, [new(1, 10, 20)], Generations(), Noon.AddSeconds(2));
        meter.Observe(clients, [], Generations(), Noon.AddDays(1));
        Assert.Equal(default, meter.Of(1, "day", Noon.AddDays(1)).Used);
        Assert.Equal(new ClientUsage(10, 20), meter.Of(1, "month", Noon.AddDays(1)).Used);
    }

    [Fact]
    public void MovingTheClockBackDoesNotRefundUsage()
    {
        var meter = new TrafficPeriodMeter();
        var clients = new[] { Client(1) };
        meter.Observe(clients, [new(1, 0, 0)], Generations(), Noon);
        meter.Observe(clients, [new(1, 10, 20)], Generations(), Noon.AddSeconds(2));
        meter.Observe(clients, [new(1, 20, 30)], Generations(), Noon.AddDays(-1));
        Assert.Equal(new ClientUsage(20, 30), meter.Of(1, "day", Noon).Used);
        Assert.Equal(TrafficPeriods.At("day", Noon).Start, meter.Of(1, "day", Noon).StartedUtc);
    }

    [Theory]
    [InlineData("throttle")]
    [InlineData("block")]
    public void ExhaustionUsesTheEffectivePolicyAndDoesNotChangeLegacyUsage(string action)
    {
        var clock = new Clock(Noon);
        var ledger = new TrafficLedger(clock);
        var template = new ClientTemplate
        {
            Id = 4, Name = "limited", Limits = new TrafficLimits
            {
                Enabled = true, QuotaBytes = 100, Period = "month", QuotaAction = action,
                UploadBitsPerSecond = 1000, DownloadBitsPerSecond = 2000,
                ExceededUploadBitsPerSecond = 200, ExceededDownloadBitsPerSecond = 300,
            },
        };
        var client = Client(1) with { TemplateId = template.Id, DailyLimit = 1 };
        ledger.SetLimits([client], [template]);
        ledger.Observe([client], [new(1, 0, 0)], Noon, Generations());
        ledger.Observe([client], [new(1, 60, 60)], Noon.AddSeconds(2), Generations());
        var state = ledger.Of(client);
        Assert.Equal(action == "block", ledger.IsSpent(client));
        Assert.True(state.Limits!.Inherited);
        Assert.True(state.Limits.IsExhausted);
        Assert.Equal(action == "throttle", state.Limits.IsThrottled);
        Assert.Equal(action == "block" ? 1000 : 200, state.Limits.UploadBitsPerSecond);
        Assert.Equal(new ClientUsage(60, 60), state.Used);
    }

    [Fact]
    public void ChangingATemplateOrChoosingAnOverrideDoesNotRefundTheWindow()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var policy = new TrafficLimits { Enabled = true, Period = "month", QuotaBytes = 100, QuotaAction = "block" };
        var template = new ClientTemplate { Id = 3, Name = "shared-defaults", Limits = policy };
        var client = Client(1) with { TemplateId = 3 };
        ledger.SetLimits([client], [template]);
        ledger.Observe([client], [new(1, 0, 0)], Noon, Generations());
        ledger.Observe([client], [new(1, 70, 80)], Noon.AddSeconds(2), Generations());
        var overridden = client with { OverrideLimits = true, Limits = policy with { QuotaBytes = 200 } };
        ledger.SetLimits([overridden], [template]);
        Assert.Equal(150UL, ledger.Of(overridden).Limits!.UsedBytes);
        Assert.Equal(50UL, ledger.Of(overridden).Limits!.RemainingBytes);
        Assert.False(ledger.Of(overridden).Limits!.Inherited);
    }

    [Fact]
    public async Task PeriodUsageAndCounterGenerationSurviveStorageAndReset()
    {
        using var bench = new Bench(now: Noon);
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "limited") with { Address = ["10.8.0.2/32"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var generations = new Dictionary<long, string> { [client.Id] = "boot:index:key" };
        var meter = new TrafficPeriodMeter();
        meter.Observe([client], [new(client.Id, 0, 0)], generations, Noon);
        meter.Observe([client], [new(client.Id, 10, 20)], generations, Noon.AddSeconds(2));
        var store = new TrafficPeriodStore(bench.Db);
        await store.SaveAsync(meter.Pending(), CancellationToken.None).ConfigureAwait(false);
        var restarted = new TrafficPeriodMeter();
        restarted.Load(await store.ReadAsync(CancellationToken.None).ConfigureAwait(false));
        restarted.Observe([client], [new(client.Id, 15, 30)], generations, Noon.AddSeconds(4));
        Assert.Equal(new ClientUsage(15, 30), restarted.Of(client.Id, "month", Noon).Used);
        var reset = restarted.Reset(client.Id, "month", Noon);
        await store.SaveAsync([reset], CancellationToken.None).ConfigureAwait(false);
        restarted.Replace(reset);
        restarted.Observe([client], [new(client.Id, 17, 33)], generations, Noon.AddSeconds(6));
        Assert.Equal(new ClientUsage(2, 3), restarted.Of(client.Id, "month", Noon).Used);
        Assert.Equal(new ClientUsage(17, 33), restarted.Of(client.Id, "week", Noon).Used);
    }

    private static TunnelClient Client(long id) => ClientDefaults.Fresh(1, $"client{id}") with { Id = id };

    private static IReadOnlyDictionary<long, string> Generations() => new Dictionary<long, string> { [1] = "generation-1", [2] = "generation-2" };
}
