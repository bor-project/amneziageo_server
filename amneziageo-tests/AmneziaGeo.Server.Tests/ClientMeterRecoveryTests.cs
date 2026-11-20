using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientMeterRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQuotaTransitionIsRetriedAfterTheInterfaceReturns(bool restore)
    {
        using var bench = new Bench(now: new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "limited") with { Address = ["10.8.0.2/32"], DailyLimit = 1000 },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var kernel = new Kernel();
        var network = new Ledger();
        network.Links.Add("awg1");
        var ledger = new TrafficLedger(bench.Clock);
        var host = new ClientHost(network, kernel, new InterfaceFile(new InterfaceFileOptions { KeepFile = false }), bench.Clock, ledger);
        var meter = new ClientMeter(bench.Scopes, kernel, host, ledger, bench.Clock, NullLogger<ClientMeter>.Instance);
        kernel.Hold(Device(client.PublicKey, 0));
        await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);
        if (!restore)
        {
            network.Links.Remove("awg1");
        }

        kernel.Hold(Device(client.PublicKey, 600));
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);
        if (restore)
        {
            network.Links.Remove("awg1");
            bench.Clock.Pass(TimeSpan.FromDays(1));
            await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);
        }

        kernel.Updates.Clear();
        network.Links.Add("awg1");
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(!restore, ledger.IsSpent(client));
        Assert.Contains(kernel.Updates.SelectMany(update => update.Peers),
            peer => peer.PublicKey == client.PublicKey && peer.Remove == !restore);
    }

    [Fact]
    public async Task AClientWhoseQuotaIsSpentWhileSyncingIsNotAddedAgain()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var client = ClientDefaults.Fresh(1, "limited") with
        {
            Id = 1, Address = ["10.8.0.2/32"], OverrideLimits = true,
            Limits = new TrafficLimits(Enabled: true, QuotaBytes: 100, QuotaAction: "block"),
        };
        var ledger = new TrafficLedger(new Clock(now));
        ledger.SetLimits([client], []);
        ledger.Observe([client], [new(1, 0, 0)], now);
        var kernel = new Kernel();
        var network = new Ledger();
        network.Links.Add("awg1");
        network.BeforeLinkCheck = () => ledger.Observe([client], [new(1, 60, 60)], now.AddSeconds(2));
        var host = new ClientHost(network, kernel, new InterfaceFile(new InterfaceFileOptions { KeepFile = false }), new Clock(now), ledger);
        var endpoint = ConfigDefaults.Fresh("awg1") with { Id = 1, Address = ["10.8.0.1/24"] };

        var sync = await host.SyncAsync(endpoint, [client], [], CancellationToken.None).ConfigureAwait(false);

        Assert.True(sync.IsDone);
        Assert.Contains(kernel.Updates.SelectMany(update => update.Peers), peer => peer.PublicKey == client.PublicKey && peer.Remove);
        Assert.DoesNotContain(kernel.Updates.SelectMany(update => update.Peers), peer => peer.PublicKey == client.PublicKey && !peer.Remove);
    }

    [Fact]
    public async Task AQuotaBlockDoesNotDependOnWritingTheInterfaceFile()
    {
        var folder = Directory.CreateTempSubdirectory("ag804-file-refusal-");
        try
        {
            var path = Path.Combine(folder.FullName, "awg1.conf");
            await File.WriteAllTextAsync(path, "[Interface]\nListenPort = 51820\n").ConfigureAwait(false);
            using var heldFile = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
            var client = ClientDefaults.Fresh(1, "limited") with
            {
                Id = 1, Address = ["10.8.0.2/32"], OverrideLimits = true,
                Limits = new TrafficLimits(Enabled: true, QuotaBytes: 100, QuotaAction: "block"),
            };
            var ledger = new TrafficLedger(new Clock(now));
            ledger.SetLimits([client], []);
            ledger.Observe([client], [new(1, 0, 0)], now);
            ledger.Observe([client], [new(1, 60, 60)], now.AddSeconds(2));
            var kernel = new Kernel();
            var network = new Ledger();
            network.Links.Add("awg1");
            var file = new InterfaceFile(new InterfaceFileOptions { Directory = folder.FullName });
            var host = new ClientHost(network, kernel, file, new Clock(now), ledger);
            var endpoint = ConfigDefaults.Fresh("awg1") with { Id = 1, Address = ["10.8.0.1/24"] };

            var sync = await host.SyncAsync(endpoint, [client], [], CancellationToken.None).ConfigureAwait(false);

            Assert.False(sync.IsDone);
            Assert.Contains(kernel.Updates.SelectMany(update => update.Peers), peer => peer.PublicKey == client.PublicKey && peer.Remove);
            Assert.DoesNotContain(kernel.Updates.SelectMany(update => update.Peers), peer => peer.PublicKey == client.PublicKey && !peer.Remove);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static AwgDevice Device(string key, ulong bytes) => new()
    {
        Name = "awg1",
        Peers = [new AwgPeer { PublicKey = key, RxBytes = bytes, TxBytes = bytes }],
    };
}
