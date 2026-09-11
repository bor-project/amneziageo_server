using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class TrafficTests
{
    private static readonly DateOnly Monday = new(2026, 9, 14);

    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstReadingOfAPeerCountsNothing()
    {
        var meter = new TrafficMeter(Monday);

        meter.Observe([new TrafficReading(1, 5000, 7000)], Noon);

        Assert.Equal(default, meter.Used(1));
        Assert.Equal(default, meter.Rate(1));
    }

    [Fact]
    public void ACounterThatGrewAddsWhatItGrewByAndHowFast()
    {
        var meter = new TrafficMeter(Monday);

        meter.Observe([new TrafficReading(1, 100, 200)], Noon);
        meter.Observe([new TrafficReading(1, 150, 260)], Noon.AddSeconds(2));

        Assert.Equal(new ClientUsage(50, 60), meter.Used(1));
        Assert.Equal(new TrafficRate(25, 30), meter.Rate(1));
    }

    [Fact]
    public void ACounterThatStartedOverIsCountedWhole()
    {
        var meter = new TrafficMeter(Monday);

        meter.Observe([new TrafficReading(1, 1000, 1000)], Noon);
        meter.Observe([new TrafficReading(1, 300, 40)], Noon.AddSeconds(2));

        Assert.Equal(new ClientUsage(300, 40), meter.Used(1));
    }

    [Fact]
    public void WhatAPeerMovedWhileThePanelWasDownCountsFromTheCountersWrittenLast()
    {
        var meter = new TrafficMeter(Monday);
        meter.Load([new TrafficDay(1, Monday, new ClientUsage(10, 20), new ClientUsage(100, 100))]);

        meter.Observe([new TrafficReading(1, 180, 150)], Noon);

        Assert.Equal(new ClientUsage(90, 70), meter.Used(1));
        Assert.Equal(default, meter.Rate(1));
    }

    [Fact]
    public void TheTrafficOfAnotherDayStaysOutOfToday()
    {
        var meter = new TrafficMeter(Monday);
        meter.Load([new TrafficDay(1, Monday.AddDays(-1), new ClientUsage(500, 500), new ClientUsage(100, 100))]);

        meter.Observe([new TrafficReading(1, 110, 100)], Noon);

        Assert.Equal(new ClientUsage(10, 0), meter.Used(1));
    }

    [Fact]
    public void ANewDayStartsWithNothingCounted()
    {
        var meter = new TrafficMeter(Monday);
        meter.Observe([new TrafficReading(1, 0, 0)], Noon);
        meter.Observe([new TrafficReading(1, 100, 100)], Noon.AddSeconds(2));

        meter.Turn(Monday.AddDays(1));
        var quiet = meter.Pending();
        meter.Observe([new TrafficReading(1, 130, 100)], Noon.AddSeconds(4));

        Assert.Empty(quiet);
        Assert.Equal(new ClientUsage(30, 0), meter.Used(1));
        Assert.Equal(Monday.AddDays(1), Assert.Single(meter.Pending()).Day);
    }

    [Fact]
    public void APeerThatIsGoneMovesNothingAndKeepsItsDay()
    {
        var meter = new TrafficMeter(Monday);
        meter.Observe([new TrafficReading(1, 0, 0)], Noon);
        meter.Observe([new TrafficReading(1, 100, 100)], Noon.AddSeconds(2));

        meter.Observe([], Noon.AddSeconds(4));

        Assert.Equal(default, meter.Rate(1));
        Assert.Equal(new ClientUsage(100, 100), meter.Used(1));
    }

    [Fact]
    public void ADayIsWrittenOnceUntilThePeerMovesAgain()
    {
        var meter = new TrafficMeter(Monday);
        meter.Observe([new TrafficReading(1, 0, 0)], Noon);
        var first = meter.Pending();
        meter.Written(first);

        meter.Observe([new TrafficReading(1, 0, 0)], Noon.AddSeconds(2));
        var still = meter.Pending();
        meter.Observe([new TrafficReading(1, 10, 0)], Noon.AddSeconds(4));

        Assert.Single(first);
        Assert.Empty(still);
        Assert.Equal(new ClientUsage(10, 0), Assert.Single(meter.Pending()).Used);
    }

    [Fact]
    public void AClientThePanelNoLongerHoldsIsForgotten()
    {
        var meter = new TrafficMeter(Monday);
        meter.Observe([new TrafficReading(1, 0, 0), new TrafficReading(2, 0, 0)], Noon);

        meter.Keep(new HashSet<long> { 2 });

        Assert.Equal(2, Assert.Single(meter.Pending()).ClientId);
    }

    [Fact]
    public void TheLimitCountsAClientTogetherWithItsDevices()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var owner = Client(1, null, 100);
        var device = Client(2, 1, 100);
        var other = Client(3, null, 100);
        ledger.Observe(
            [owner, device, other],
            [new TrafficReading(1, 0, 0), new TrafficReading(2, 0, 0), new TrafficReading(3, 0, 0)],
            Noon);

        ledger.Observe(
            [owner, device, other],
            [new TrafficReading(1, 30, 30), new TrafficReading(2, 20, 20), new TrafficReading(3, 30, 30)],
            Noon.AddSeconds(2));

        Assert.True(ledger.IsSpent(owner));
        Assert.True(ledger.IsSpent(device));
        Assert.False(ledger.IsSpent(other));
        Assert.Equal(new ClientUsage(50, 50), ledger.Group(device));
        Assert.Equal(new ClientUsage(20, 20), ledger.Of(device).Used);
        Assert.Equal(new TrafficRate(10, 10), ledger.Of(device).Rate);
    }

    [Fact]
    public void AClientWithoutALimitIsNeverSpent()
    {
        var ledger = new TrafficLedger(new Clock(Noon));
        var client = Client(1, null, 0);
        ledger.Observe([client], [new TrafficReading(1, 0, 0)], Noon);

        ledger.Observe([client], [new TrafficReading(1, ulong.MaxValue / 4, ulong.MaxValue / 4)], Noon.AddSeconds(2));

        Assert.False(ledger.IsSpent(client));
    }

    [Fact]
    public void TheDayTurnsAtMidnightOfTheHost()
    {
        var clock = new Clock(new DateTimeOffset(2026, 9, 14, 20, 30, 0, TimeSpan.Zero))
        {
            Zone = TimeZoneInfo.CreateCustomTimeZone("host", TimeSpan.FromHours(3), "host", "host"),
        };
        var ledger = new TrafficLedger(clock);
        var before = ledger.HostDay();

        clock.Pass(TimeSpan.FromHours(1));

        Assert.Equal(Monday, before);
        Assert.Equal(Monday.AddDays(1), ledger.HostDay());
    }

    [Fact]
    public async Task AClientThatUsedUpItsLimitComesOffTheInterfaceAndTheFile()
    {
        var folder = Directory.CreateTempSubdirectory("amneziageo-traffic-");
        try
        {
            var path = Path.Combine(folder.FullName, "awg1.conf");
            await File.WriteAllTextAsync(path, "[Interface]\nListenPort = 51820\n");
            var kernel = new Kernel();
            var network = new Ledger();
            network.Links.Add("awg1");
            var ledger = new TrafficLedger(new Clock(Noon));
            var spent = Client(1, null, 100) with { Name = "milena" };
            var fine = Client(2, null, 0) with { Name = "bogdan" };
            ledger.Observe([spent, fine], [new TrafficReading(1, 0, 0), new TrafficReading(2, 0, 0)], Noon);
            ledger.Observe([spent, fine], [new TrafficReading(1, 100, 100), new TrafficReading(2, 100, 100)], Noon.AddSeconds(2));
            var file = new InterfaceFile(new InterfaceFileOptions { Directory = folder.FullName });
            var host = new ClientHost(network, kernel, file, new Clock(Noon), ledger);

            var sync = await host.SyncAsync(Endpoint(), [spent, fine], [], CancellationToken.None);
            var text = await File.ReadAllTextAsync(path);

            Assert.True(sync.IsDone, sync.Message);
            Assert.Contains(kernel.Updates[0].Peers, peer => peer.PublicKey == spent.PublicKey && peer.Remove);
            Assert.Equal(new[] { fine.PublicKey }, kernel.Updates[1].Peers.Select(peer => peer.PublicKey));
            Assert.Contains("# bogdan", text, StringComparison.Ordinal);
            Assert.DoesNotContain("# milena", text, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task TheLastDayOfEveryClientReadsBackWithTheCountersOfItsPeer()
    {
        using var bench = new Bench();
        var client = await ClientAsync(bench);
        var store = new TrafficStore(bench.Db);

        await store.SaveAsync(
            [new TrafficDay(client.Id, Monday.AddDays(-1), new ClientUsage(1, 2), new ClientUsage(3, 4))],
            CancellationToken.None);
        await store.SaveAsync(
            [new TrafficDay(client.Id, Monday, new ClientUsage(5, 6), new ClientUsage(7, 8))],
            CancellationToken.None);
        await store.SaveAsync(
            [new TrafficDay(client.Id, Monday, new ClientUsage(9, 10), new ClientUsage(11, 12))],
            CancellationToken.None);
        var latest = await store.LatestAsync(CancellationToken.None);

        Assert.Equal(
            new TrafficDay(client.Id, Monday, new ClientUsage(9, 10), new ClientUsage(11, 12)),
            Assert.Single(latest));
    }

    [Fact]
    public async Task TheTrafficOfAClientThatIsGoneIsNotWrittenAndLeavesWithIt()
    {
        using var bench = new Bench();
        var client = await ClientAsync(bench);
        var store = new TrafficStore(bench.Db);
        await store.SaveAsync(
            [new TrafficDay(client.Id, Monday, new ClientUsage(1, 1), new ClientUsage(1, 1))],
            CancellationToken.None);

        await bench.Clients.RemoveAsync(client.Id, CancellationToken.None);
        await store.SaveAsync(
            [new TrafficDay(client.Id + 100, Monday, new ClientUsage(1, 1), new ClientUsage(1, 1))],
            CancellationToken.None);

        Assert.Empty(await store.LatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheDailyLimitOfAClientGoesOverToItsDevices()
    {
        using var bench = new Bench();
        var owner = await ClientAsync(bench, 1000);
        var device = await bench.Clients.AddDeviceAsync(owner.Id, CancellationToken.None);

        await bench.Clients.ChangeAsync(owner.Id, owner with { DailyLimit = 5000 }, CancellationToken.None);
        var followed = await bench.Clients.FindAsync(device.Record!.Id, CancellationToken.None);
        var kept = await bench.Clients.ChangeAsync(device.Record.Id, followed! with { DailyLimit = 1 }, CancellationToken.None);

        Assert.Equal(1000, device.Record.DailyLimit);
        Assert.Equal(5000, followed.DailyLimit);
        Assert.Equal(5000, kept.Record!.DailyLimit);
    }

    [Fact]
    public async Task ALimitBelowNothingIsRefused()
    {
        using var bench = new Bench();
        var owner = await ClientAsync(bench);

        var refused = await bench.Clients.ChangeAsync(owner.Id, owner with { DailyLimit = -1 }, CancellationToken.None);

        Assert.Equal("bad-client-limit", refused.Code);
    }

    [Fact]
    public void ARequestWithoutALimitKeepsTheOneTheClientHeld()
    {
        var held = ClientDefaults.Fresh(1, "milena") with { DailyLimit = 1000 };
        var request = new ClientRequest(1, "milena", held.PrivateKey, null, null, ["10.8.0.2/32"], true, null);

        Assert.Equal(1000, ClientAnswers.Draft(request, held).DailyLimit);
        Assert.Equal(0, ClientAnswers.Draft(request, null).DailyLimit);
        Assert.Equal(5, ClientAnswers.Draft(request with { DailyLimit = 5 }, held).DailyLimit);
    }

    [Fact]
    public async Task TheMeterTakesOffAClientThatUsedUpItsLimitAndLaysItBackTheNextDay()
    {
        using var bench = new Bench(now: new DateTimeOffset(2026, 9, 14, 23, 59, 50, TimeSpan.Zero));
        var client = await ClientAsync(bench, 1000);
        var kernel = new Kernel();
        var network = new Ledger();
        network.Links.Add("awg1");
        var ledger = new TrafficLedger(bench.Clock);
        var file = new InterfaceFile(new InterfaceFileOptions { KeepFile = false });
        var host = new ClientHost(network, kernel, file, bench.Clock, ledger);
        var meter = new ClientMeter(bench.Scopes, kernel, host, ledger, bench.Clock, NullLogger<ClientMeter>.Instance);

        kernel.Hold(Device(client.PublicKey, 0, 0));
        await meter.MeasureAsync(CancellationToken.None);
        kernel.Hold(Device(client.PublicKey, 600, 600));
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(CancellationToken.None);
        var cut = Laid(kernel, client.PublicKey, true);
        var spent = ledger.IsSpent(client);

        kernel.Updates.Clear();
        bench.Clock.Pass(TimeSpan.FromSeconds(10));
        await meter.MeasureAsync(CancellationToken.None);
        var back = Laid(kernel, client.PublicKey, false);
        var written = await new TrafficStore(bench.Db).LatestAsync(CancellationToken.None);

        Assert.True(cut);
        Assert.True(spent);
        Assert.False(ledger.IsSpent(client));
        Assert.True(back);
        Assert.Equal(
            new TrafficDay(client.Id, Monday, new ClientUsage(600, 600), new ClientUsage(600, 600)),
            Assert.Single(written));
    }

    private static TunnelClient Client(long id, long? parent, long limit) =>
        ClientDefaults.Fresh(1, $"client{id}") with
        {
            Id = id,
            ParentId = parent,
            DailyLimit = limit,
            Address = [$"10.8.0.{id + 1}/32"],
        };

    private static ServerConfig Endpoint() =>
        ConfigDefaults.Fresh("awg1") with { Id = 1, Address = ["10.8.0.1/24"] };

    private static AwgDevice Device(string key, ulong rx, ulong tx) => new()
    {
        Name = "awg1",
        Peers = [new AwgPeer { PublicKey = key, RxBytes = rx, TxBytes = tx }],
    };

    private static bool Laid(Kernel kernel, string key, bool removed) =>
        kernel.Updates.SelectMany(update => update.Peers).Any(peer => peer.PublicKey == key && peer.Remove == removed);

    private static async Task<TunnelClient> ClientAsync(Bench bench, long limit = 0)
    {
        var endpoint = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None);
        var added = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "milena") with
            {
                Address = ["10.8.0.2/32"],
                MultiDevice = true,
                DailyLimit = limit,
            },
            CancellationToken.None);

        return added.Record!;
    }
}
