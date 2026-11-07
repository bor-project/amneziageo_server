using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Routing.Outbound;
using AmneziaGeo.Server.Routing.Probe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class ProbeHealthTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AlternatingUnansweredChecksWarnWithoutChangingTheRoutingVerdict()
    {
        var live = new ProbeLive();
        var warnings = 0;
        for (var n = 0; n < 6; n++)
        {
            var outcome = n % 2 == 0 ? ProbeOutcome.Reached : ProbeOutcome.Missed;
            var attempt = Attempt(live, Tunnel());
            var result = live.Complete(attempt, outcome, Noon.AddSeconds(n));
            warnings += result.Warning ? 1 : 0;
            Assert.False(result.CarriesChanged);
        }

        var reading = Assert.IsType<ProbeReading>(live.Find(Tunnel()));
        Assert.Equal(ProbeHealth.Degraded, reading.Health);
        Assert.True(reading.IsReached);
        Assert.Equal(6, reading.Attempts);
        Assert.Equal(3, reading.Missed);
        Assert.Equal(1, warnings);
        Assert.True(new OutboundState("test", true, "", Noon, 1, 1, true, "", reading).Carries);
    }

    [Fact]
    public void FewerThanSixMixedChecksDoNotDeclareDegradation()
    {
        var live = new ProbeLive();
        foreach (var outcome in new[] { ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached })
        {
            live.Complete(Attempt(live, Tunnel()), outcome, Noon);
        }

        Assert.Equal(ProbeHealth.Healthy, live.Find(Tunnel())?.Health);
        Assert.Equal(5, live.Find(Tunnel())?.Attempts);
    }

    [Fact]
    public void RecoveryWaitsForFourAnswersEvenWhenTheWindowAlreadyImproved()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached,
            ProbeOutcome.Reached, ProbeOutcome.Reached, ProbeOutcome.Missed);
        Assert.Equal(ProbeHealth.Degraded, live.Find(Tunnel())?.Health);

        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Reached, ProbeOutcome.Reached);
        Assert.Equal(ProbeHealth.Degraded, live.Find(Tunnel())?.Health);
        var recovered = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Reached, Noon.AddMinutes(1));

        Assert.True(recovered.Recovered);
        Assert.Equal(ProbeHealth.Healthy, recovered.Reading?.Health);
    }

    [Fact]
    public void RecoveryRequiresLessThanThirtyPercentUnansweredChecks()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached,
            ProbeOutcome.Missed, ProbeOutcome.Reached, ProbeOutcome.Missed);
        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Reached, ProbeOutcome.Reached, ProbeOutcome.Reached);

        Assert.Equal(3, live.Find(Tunnel())?.Missed);
        Assert.Equal(10, live.Find(Tunnel())?.Attempts);
        Assert.Equal(ProbeHealth.Degraded, live.Find(Tunnel())?.Health);
        Feed(live, ProbeOutcome.Reached);
        Assert.Equal(ProbeHealth.Healthy, live.Find(Tunnel())?.Health);
    }

    [Fact]
    public void TotalFailureKeepsTheExistingTwoMissesAndOneAnswerRoutingPolicy()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Missed);
        Assert.True(live.Find(Tunnel())?.IsReached);
        Assert.Equal(ProbeHealth.Unknown, live.Find(Tunnel())?.Health);

        var down = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Missed, Noon);
        Assert.True(down.CarriesChanged);
        Assert.False(down.Reading?.IsReached);
        Assert.Equal(ProbeHealth.Unavailable, down.Reading?.Health);

        var up = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Reached, Noon.AddSeconds(1));
        Assert.True(up.CarriesChanged);
        Assert.True(up.Reading?.IsReached);
        Assert.Equal(ProbeHealth.Degraded, up.Reading?.Health);
    }

    [Fact]
    public void TheWindowKeepsOnlyTwelveResultsAndTheLastAnswerTime()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Missed, ProbeOutcome.Missed);
        for (var n = 0; n < 15; n++)
        {
            live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Reached, Noon.AddSeconds(n));
        }

        var reading = Assert.IsType<ProbeReading>(live.Find(Tunnel()));
        Assert.Equal(12, reading.Attempts);
        Assert.Equal(0, reading.Missed);
        Assert.Equal(Noon.AddSeconds(14), reading.LastSuccess);
        Assert.Equal(ProbeHealth.Healthy, reading.Health);
    }

    [Fact]
    public void ACheckThatCouldNotBeSentChangesNeitherTheWindowNorTheTime()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Missed);
        var before = live.Find(Tunnel());

        var result = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Skipped, Noon.AddMinutes(10));

        Assert.False(result.Accepted);
        Assert.Equal(before, live.Find(Tunnel()));
    }

    [Fact]
    public void ADisabledCheckIsUnknownAndRejectsAnInflightResult()
    {
        var live = new ProbeLive();
        var original = Tunnel();
        live.Observe(original);
        var attempt = Attempt(live, original);
        Feed(live, ProbeOutcome.Missed, ProbeOutcome.Missed);
        var disabled = original with { Probe = "", ProbeEvery = 0 };

        live.Observe(disabled);

        Assert.Null(live.Find(disabled));
        Assert.Null(live.Begin(disabled));
        Assert.Null(live.Begin(original));
        Assert.False(live.Complete(attempt, ProbeOutcome.Missed, Noon).Accepted);
        Assert.Null(live.Find(original.Name));
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("period")]
    [InlineData("transport")]
    [InlineData("proxy")]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("privateKey")]
    [InlineData("peerKey")]
    [InlineData("presharedKey")]
    [InlineData("mark")]
    [InlineData("table")]
    [InlineData("enabled")]
    [InlineData("address")]
    [InlineData("mtu")]
    public void AChangedConfigurationRetiresOldResults(string field)
    {
        var live = new ProbeLive();
        var original = Tunnel();
        live.Observe(original);
        var attempt = Attempt(live, original);
        live.Complete(attempt, ProbeOutcome.Missed, Noon);
        var changed = field switch
        {
            "probe" => original with { Probe = "8.8.8.8" },
            "period" => original with { ProbeEvery = 60 },
            "transport" => original with { Kind = OutboundKind.Ws },
            "proxy" => original with { Proxy = "wss://example.com/test" },
            "host" => original with { Host = "changed.example.com" },
            "port" => original with { Port = 443 },
            "privateKey" => original with { PrivateKey = "new-private-key" },
            "peerKey" => original with { PeerKey = "new-peer-key" },
            "presharedKey" => original with { PresharedKey = "new-preshared-key" },
            "mark" => original with { Mark = 42499 },
            "table" => original with { Table = 42603 },
            "enabled" => original with { IsEnabled = false },
            "address" => original with { Address = ["10.1.1.2/32"] },
            "mtu" => original with { Mtu = 1280 },
            _ => throw new InvalidOperationException(),
        };

        live.Observe(changed);

        Assert.Null(live.Find(changed));
        Assert.False(live.IsCurrent(attempt));
        Assert.False(live.Complete(attempt, ProbeOutcome.Reached, Noon.AddSeconds(1)).Accepted);
        Assert.Null(live.Begin(original));
    }

    [Fact]
    public void UnchangedArrayValuesDoNotDiscardTheWindow()
    {
        var live = new ProbeLive();
        var original = Tunnel() with { Address = ["10.1.1.1/32"], Dns = ["1.1.1.1"] };
        live.Observe(original);
        var attempt = Attempt(live, original);
        live.Complete(attempt, ProbeOutcome.Reached, Noon);

        live.Observe(original with { Address = ["10.1.1.1/32"], Dns = ["1.1.1.1"] });

        Assert.True(live.IsCurrent(attempt));
        Assert.Equal(1, live.Find(original)?.Attempts);
    }

    [Fact]
    public void DeletingAndRecreatingTheSameNameCannotAcceptTheOldCheck()
    {
        var live = new ProbeLive();
        var original = Tunnel();
        live.Observe(original);
        var old = Attempt(live, original);
        live.Forget(original.Name);
        Assert.Null(live.Begin(original));

        live.Observe(original);
        var fresh = Attempt(live, original);

        Assert.NotEqual(old.Generation, fresh.Generation);
        Assert.False(live.Complete(old, ProbeOutcome.Missed, Noon).Accepted);
        Assert.True(live.Complete(fresh, ProbeOutcome.Reached, Noon).Accepted);
        Assert.Equal(1, live.Find(original)?.Attempts);
    }

    [Fact]
    public void SyncDropsRemovedReadingsAndPreservesUnchangedOnes()
    {
        var live = new ProbeLive();
        var one = Tunnel();
        var other = one with { Name = "other", Mark = 42499 };
        live.Sync([one, other]);
        live.Complete(Attempt(live, one), ProbeOutcome.Reached, Noon);
        var gone = Attempt(live, other);
        live.Complete(gone, ProbeOutcome.Missed, Noon);

        Assert.True(live.Sync([one]));

        Assert.Equal(1, live.Find(one)?.Attempts);
        Assert.Null(live.Find(other));
        Assert.Null(live.Begin(other));
        Assert.False(live.Complete(gone, ProbeOutcome.Missed, Noon).Accepted);
    }

    [Fact]
    public void TheApiKeepsTheOldFieldsAndAddsTheQualityFields()
    {
        var reading = new ProbeReading(true, 1, Noon)
        {
            Health = ProbeHealth.Degraded,
            Attempts = 6,
            Missed = 2,
            LastSuccess = Noon.AddSeconds(-1),
        };
        var body = OutboundAnswers.Probe(reading);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var value = json.RootElement;

        Assert.True(value.GetProperty("isReached").GetBoolean());
        Assert.Equal(1, value.GetProperty("falls").GetInt32());
        Assert.Equal(Noon, value.GetProperty("at").GetDateTimeOffset());
        Assert.Equal("degraded", value.GetProperty("health").GetString());
        Assert.Equal(6, value.GetProperty("attempts").GetInt32());
        Assert.Equal(2, value.GetProperty("missed").GetInt32());
        Assert.Equal(Noon.AddSeconds(-1), value.GetProperty("lastSuccess").GetDateTimeOffset());
        Assert.Null(OutboundAnswers.Probe(null));
    }

    [Fact]
    public async Task AResultAfterConfigurationChangedIsDiscarded()
    {
        var live = new ProbeLive();
        var link = new HeldLink();
        var original = Tunnel();
        live.Observe(original);
        var runner = Runner(link, live);

        var old = runner.RunAsync(original, CancellationToken.None);
        var held = await link.NextAsync().ConfigureAwait(false);
        var changed = original with { Host = "new.example.com" };
        live.Observe(changed);
        held.SetResult(ProbeOutcome.Missed);

        Assert.False(await old.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.Null(live.Find(changed));
    }

    [Fact]
    public async Task ConcurrentManualAndWatchChecksShareOneInFlightRequest()
    {
        var live = new ProbeLive();
        var link = new HeldLink();
        var runner = Runner(link, live);
        var first = runner.RunAsync(Tunnel(), CancellationToken.None);
        var one = await link.NextAsync().ConfigureAwait(false);
        var second = runner.RunAsync(Tunnel(), CancellationToken.None);
        Assert.Equal(1, link.Calls);

        one.SetResult(ProbeOutcome.Reached);
        await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var two = await link.NextAsync().ConfigureAwait(false);
        Assert.Equal(1, link.Active);
        two.SetResult(ProbeOutcome.Reached);
        await second.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.Equal(2, live.Find(Tunnel())?.Attempts);
        Assert.Equal(1, link.MaxActive);
    }

    [Fact]
    public async Task AQueuedOldCheckCannotReviveAnOldConfiguration()
    {
        var live = new ProbeLive();
        var link = new HeldLink();
        var original = Tunnel();
        live.Observe(original);
        var runner = Runner(link, live);
        var first = runner.RunAsync(original, CancellationToken.None);
        var one = await link.NextAsync().ConfigureAwait(false);
        var queued = runner.RunAsync(original, CancellationToken.None);
        var changed = original with { Mark = 42499 };
        live.Observe(changed);
        var fresh = runner.RunAsync(changed, CancellationToken.None);
        one.SetResult(ProbeOutcome.Missed);

        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        Assert.False(await queued.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
        var two = await link.NextAsync().ConfigureAwait(false);
        two.SetResult(ProbeOutcome.Reached);
        Assert.False(await fresh.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

        Assert.Equal(2, link.Calls);
        Assert.Equal(1, live.Find(changed)?.Attempts);
        Assert.Null(live.Find(original));
    }

    [Fact]
    public async Task ADegradationEpisodeLogsOneWarningAndOneRecovery()
    {
        var live = new ProbeLive();
        var log = new ProbeLog();
        var link = new SequenceLink(
            [ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached,
                ProbeOutcome.Missed, ProbeOutcome.Reached, ProbeOutcome.Missed,
                ProbeOutcome.Reached, ProbeOutcome.Missed]);
        var runner = new ProbeRunner(link, live, TimeProvider.System, log);
        for (var n = 0; n < 8; n++)
        {
            await runner.RunAsync(Tunnel(), CancellationToken.None).ConfigureAwait(false);
        }

        Assert.Single(log.Levels, one => one == LogLevel.Warning);
        Assert.DoesNotContain(LogLevel.Information, log.Levels);
        for (var n = 0; n < 12; n++)
        {
            await runner.RunAsync(Tunnel(), CancellationToken.None).ConfigureAwait(false);
        }

        Assert.Single(log.Levels, one => one == LogLevel.Warning);
        Assert.Single(log.Levels, one => one == LogLevel.Information);
    }

    [Fact]
    public void AStaleReadingShowsUnknownWithoutChangingTheRoutingVerdict()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached);

        Assert.Equal(ProbeHealth.Healthy, live.Find(Tunnel(), Noon.AddSeconds(62))?.Health);
        var stale = Assert.IsType<ProbeReading>(live.Find(Tunnel(), Noon.AddSeconds(63)));

        Assert.Equal(ProbeHealth.Unknown, stale.Health);
        Assert.Equal(0, stale.Attempts);
        Assert.Equal(0, stale.Missed);
        Assert.Equal(Noon, stale.At);
        Assert.Equal(Noon, stale.LastSuccess);
        Assert.True(stale.IsReached);
        Assert.Equal(0, stale.Falls);
        Assert.Equal(ProbeHealth.Healthy, live.Find(Tunnel().Name)?.Health);
        Assert.True(new OutboundState("test", true, "", Noon, 1, 1, true, "", stale).Carries);
    }

    [Fact]
    public void SkippedChecksCannotKeepAnOldHealthyReadingFresh()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached);

        var skipped = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Skipped, Noon.AddMinutes(10));
        var stale = live.Find(Tunnel(), Noon.AddMinutes(10));

        Assert.False(skipped.Accepted);
        Assert.Equal(ProbeHealth.Unknown, stale?.Health);
        Assert.Equal(Noon, stale?.At);
        Assert.True(stale?.IsReached);
    }

    [Theory]
    [InlineData(0, 62)]
    [InlineData(5, 12)]
    [InlineData(60, 122)]
    public void FreshnessUsesTwoEffectivePeriodsAndTheRequestTimeout(int every, int seconds)
    {
        var live = new ProbeLive();
        var outbound = Tunnel() with { ProbeEvery = every };
        live.Complete(Attempt(live, outbound), ProbeOutcome.Reached, Noon);

        Assert.Equal(ProbeHealth.Healthy, live.Find(outbound, Noon.AddSeconds(seconds))?.Health);
        Assert.Equal(ProbeHealth.Unknown, live.Find(outbound, Noon.AddSeconds(seconds + 1))?.Health);
    }

    [Fact]
    public void AnOldAnswerDoesNotMakeANewUnansweredWindowHealthy()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached);

        live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Missed, Noon.AddSeconds(63));
        var reading = Assert.IsType<ProbeReading>(live.Find(Tunnel(), Noon.AddSeconds(63)));

        Assert.Equal(1, reading.Attempts);
        Assert.Equal(1, reading.Missed);
        Assert.Equal(Noon, reading.LastSuccess);
        Assert.Equal(ProbeHealth.Unknown, reading.Health);
        Assert.True(reading.IsReached);
    }

    [Fact]
    public void AnExpiredDegradationWindowDoesNotTaintANewAnswer()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Reached, ProbeOutcome.Missed, ProbeOutcome.Reached,
            ProbeOutcome.Missed, ProbeOutcome.Reached, ProbeOutcome.Missed);
        Assert.Equal(ProbeHealth.Degraded, live.Find(Tunnel())?.Health);

        var result = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Reached, Noon.AddSeconds(63));

        Assert.Equal(1, result.Reading?.Attempts);
        Assert.Equal(0, result.Reading?.Missed);
        Assert.Equal(ProbeHealth.Healthy, result.Reading?.Health);
        Assert.False(result.Warning);
        Assert.False(result.Recovered);
    }

    [Fact]
    public void ExpiringTheQualityWindowPreservesTheLegacyFailureCount()
    {
        var live = new ProbeLive();
        Feed(live, ProbeOutcome.Missed, ProbeOutcome.Missed);
        var stale = live.Find(Tunnel(), Noon.AddSeconds(63));
        Assert.Equal(ProbeHealth.Unknown, stale?.Health);
        Assert.False(stale?.IsReached);
        Assert.Equal(2, stale?.Falls);

        var result = live.Complete(Attempt(live, Tunnel()), ProbeOutcome.Missed, Noon.AddSeconds(63));

        Assert.Equal(3, result.Reading?.Falls);
        Assert.Equal(1, result.Reading?.Attempts);
        Assert.Equal(ProbeHealth.Unavailable, result.Reading?.Health);
    }

    private static void Feed(ProbeLive live, params ProbeOutcome[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            live.Complete(Attempt(live, Tunnel()), outcome, Noon);
        }
    }

    private static ProbeAttempt Attempt(ProbeLive live, OutboundConfig outbound) =>
        live.Begin(outbound) ?? throw new InvalidOperationException("No current check.");

    private static OutboundConfig Tunnel() => new()
    {
        Name = "health-test",
        Kind = OutboundKind.Wg,
        Host = "example.com",
        Port = 51820,
        Probe = "1.1.1.1",
        ProbeEvery = 30,
        Mark = 42498,
        Table = 42602,
    };

    private static ProbeRunner Runner(IProbeLink link, ProbeLive live) =>
        new(link, live, TimeProvider.System, NullLogger<ProbeRunner>.Instance);

    private sealed class HeldLink : IProbeLink
    {
        private readonly ConcurrentQueue<TaskCompletionSource<ProbeOutcome>> _requests = new();

        private readonly SemaphoreSlim _started = new(0);

        private int _active;

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public int Active => Volatile.Read(ref _active);

        public int MaxActive { get; private set; }

        public async Task<ProbeOutcome> ReachAsync(IPEndPoint server, uint mark, string name, TimeSpan wait, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref _active));
            var answer = new TaskCompletionSource<ProbeOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Enqueue(answer);
            _started.Release();
            try
            {
                return await answer.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task<TaskCompletionSource<ProbeOutcome>> NextAsync()
        {
            await _started.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            return _requests.TryDequeue(out var request) ? request : throw new InvalidOperationException("No request.");
        }
    }

    private sealed class SequenceLink : IProbeLink
    {
        private readonly Queue<ProbeOutcome> _outcomes;

        /// <summary>
        /// ctor
        /// </summary>
        public SequenceLink(IEnumerable<ProbeOutcome> outcomes)
        {
            _outcomes = new Queue<ProbeOutcome>(outcomes);
        }

        public Task<ProbeOutcome> ReachAsync(IPEndPoint server, uint mark, string name, TimeSpan wait, CancellationToken ct) =>
            Task.FromResult(_outcomes.Count > 0 ? _outcomes.Dequeue() : ProbeOutcome.Reached);
    }

    private sealed class ProbeLog : ILogger<ProbeRunner>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
