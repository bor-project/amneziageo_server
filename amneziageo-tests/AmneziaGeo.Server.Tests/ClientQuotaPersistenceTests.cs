using System.Net;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientQuotaPersistenceTests
{
    [Theory]
    [InlineData("legacy")]
    [InlineData("block")]
    [InlineData("throttle")]
    public async Task AQuotaIsEnforcedWhenItsCheckpointFailsAndItsUsageSurvivesTheRetry(string action)
    {
        var failure = new WriteFailure { PeriodsOnly = action != "legacy" };
        using var bench = new Bench(
            now: new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero),
            configure: services => services.ConfigureDbContext<AppDbContext>(
                options => options.AddInterceptors(failure)));
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var policy = action == "legacy" ? null : new TrafficLimits(
            Enabled: true,
            UploadBitsPerSecond: 1_000_000,
            DownloadBitsPerSecond: 2_000_000,
            QuotaBytes: 1000,
            Period: TrafficLimitPeriod.Month,
            ExceededUploadBitsPerSecond: 128_000,
            ExceededDownloadBitsPerSecond: 256_000,
            QuotaAction: action);
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "limited") with
            {
                Address = ["10.8.0.2/32"],
                DailyLimit = action == "legacy" ? 1000 : 1,
                OverrideLimits = policy is not null,
                Limits = policy,
            },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var kernel = new Kernel();
        var network = new Ledger();
        network.Links.Add(endpoint.Name);
        var ledger = new TrafficLedger(bench.Clock);
        var host = new ClientHost(network, kernel,
            new InterfaceFile(new InterfaceFileOptions { KeepFile = false }), bench.Clock, ledger);
        var meter = new ClientMeter(bench.Scopes, kernel, host, ledger, bench.Clock,
            NullLogger<ClientMeter>.Instance);
        kernel.Hold(Device(client.PublicKey, 0));

        await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);
        kernel.Updates.Clear();
        kernel.Hold(Device(client.PublicKey, 600));
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        failure.Armed = true;

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => meter.MeasureAsync(CancellationToken.None)).ConfigureAwait(false);

        Assert.Equal(WriteFailure.Message, error.Message);
        Assert.True(failure.Failures > 0);
        Assert.Equal(new ClientUsage(600, 600), ledger.Used(client));
        var blocked = ledger.IsSpent(client);
        if (action == "throttle" && !blocked)
        {
            var limits = ledger.Of(client).Limits!;
            Assert.True(limits.IsExhausted);
            Assert.True(limits.IsThrottled);
            Assert.True(limits.Applied);
            Assert.Equal(128_000, limits.UploadBitsPerSecond);
            Assert.Equal(256_000, limits.DownloadBitsPerSecond);
        }
        else
        {
            Assert.True(blocked);
            Assert.Contains(kernel.Updates.SelectMany(update => update.Peers),
                peer => peer.PublicKey == client.PublicKey && peer.Remove);
            Assert.DoesNotContain(kernel.Updates.SelectMany(update => update.Peers),
                peer => peer.PublicKey == client.PublicKey && !peer.Remove);
        }

        if (blocked)
        {
            kernel.Hold(new AwgDevice { Name = endpoint.Name, Peers = [] });
        }

        failure.Armed = false;
        bench.Clock.Pass(TimeSpan.FromSeconds(2));
        await meter.MeasureAsync(CancellationToken.None).ConfigureAwait(false);
        var days = await new TrafficStore(bench.Db).LatestAsync(CancellationToken.None).ConfigureAwait(false);
        var periods = await new TrafficPeriodStore(bench.Db).ReadAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(new ClientUsage(600, 600), Assert.Single(days, day => day.ClientId == client.Id).Used);
        Assert.Equal(3, periods.Count(period => period.ClientId == client.Id));
        Assert.All(periods.Where(period => period.ClientId == client.Id),
            period => Assert.Equal(new ClientUsage(600, 600), period.Used));
        Assert.Equal(new ClientUsage(600, 600), ledger.Used(client));
        if (action == "throttle")
        {
            var limits = ledger.Of(client).Limits!;
            Assert.False(ledger.IsSpent(client));
            Assert.True(limits.IsThrottled);
            Assert.True(limits.Applied);
            Assert.Equal(1200UL, limits.UsedBytes);
            Assert.Equal(128_000, limits.UploadBitsPerSecond);
            Assert.Equal(256_000, limits.DownloadBitsPerSecond);
        }
        else
        {
            Assert.True(ledger.IsSpent(client));
        }
    }

    [Fact]
    public async Task AGuardCutDoesNotRestoreAPeerWhoseQuotaWasSpentBeforeItTookTheCounterGate()
    {
        using var bench = new Bench(now: new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var client = ClientDefaults.Fresh(endpoint.Id, "limited") with
        {
            Id = 1,
            Address = ["10.8.0.2/32"],
            OverrideLimits = true,
            Limits = new TrafficLimits(Enabled: true, QuotaBytes: 100, QuotaAction: TrafficQuotaAction.Block),
        };
        var ledger = new TrafficLedger(bench.Clock);
        ledger.SetLimits([client], []);
        ledger.Observe([client], [new TrafficReading(client.Id, 0, 0)], bench.Clock.GetUtcNow());
        var kernel = new Kernel();
        var logger = new QuotaBoundaryLogger
        {
            OnWarning = () => ledger.Observe([client],
                [new TrafficReading(client.Id, 60, 60)], bench.Clock.GetUtcNow()),
        };
        var guard = new ClientGuard(bench.Scopes, kernel, new Ledger(),
            new GuardOptions { IsEnabled = true }, bench.Clock, logger, ledger: ledger);

        for (var step = 0; step < 5; step++)
        {
            kernel.Hold(new AwgDevice
            {
                Name = endpoint.Name,
                ListenPort = 51820,
                Peers =
                [
                    new AwgPeer
                    {
                        PublicKey = client.PublicKey,
                        Endpoint = IPEndPoint.Parse(step % 2 == 0 ? "10.99.1.2:40000" : "10.99.3.2:40001"),
                        LastHandshake = bench.Clock.GetUtcNow(),
                        RxBytes = (ulong)step + 1,
                    },
                ],
            });
            await guard.LookAsync(CancellationToken.None).ConfigureAwait(false);
            bench.Clock.Pass(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(1, logger.Warnings);
        Assert.True(ledger.IsSpent(client));
        Assert.DoesNotContain(kernel.Updates.SelectMany(update => update.Peers),
            peer => peer.PublicKey == client.PublicKey && !peer.Remove);
    }

    private sealed class QuotaBoundaryLogger : ILogger<ClientGuard>
    {
        public Action? OnWarning { get; init; }

        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning
                && formatter(state, exception).StartsWith("the configuration of ", StringComparison.Ordinal))
            {
                Warnings++;
                OnWarning?.Invoke();
            }
        }
    }

    private static AwgDevice Device(string key, ulong bytes) => new()
    {
        Name = "awg1",
        Index = 42,
        Peers = [new AwgPeer { PublicKey = key, RxBytes = bytes, TxBytes = bytes }],
    };

    private sealed class WriteFailure : SaveChangesInterceptor
    {
        public const string Message = "the quota checkpoint failed";

        public bool PeriodsOnly { get; init; }

        public bool Armed { get; set; }

        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context?.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified
                && (PeriodsOnly ? entry.Entity is TrafficPeriodEntity : entry.Entity is TrafficEntity)) == true)
            {
                Failures++;

                throw new DbUpdateException(Message);
            }

            return ValueTask.FromResult(result);
        }
    }
}
