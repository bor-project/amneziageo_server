using System.Net;
using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Guard;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public class DeviceTests
{
    private const string Key = "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=";

    private static readonly DateTimeOffset Start = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    [Fact]
    public void AClientIsAnsweredWithoutAParent()
    {
        var client = ClientDefaults.Fresh(1, "milena") with { Id = 2, Address = ["10.8.0.2/32"] };

        var answer = ClientAnswers.Client(client, "awg1", ClientState.Missing(client), false, [], ClientTraffic.None);
        var text = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"name\":\"milena\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"parentId\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientIsAnsweredWithoutNetworksBehindIt()
    {
        var client = ClientDefaults.Fresh(1, "milena") with { Id = 2, Address = ["10.8.0.2/32"] };

        var answer = ClientAnswers.Client(client, "awg1", ClientState.Missing(client), false, [], ClientTraffic.None);
        var text = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"inbound\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"routes\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClientIsAnsweredWithoutPortsOfTheHost()
    {
        var client = ClientDefaults.Fresh(1, "milena") with { Id = 2, Address = ["10.8.0.2/32"] };

        var answer = ClientAnswers.Client(client, "awg1", ClientState.Missing(client), false, [], ClientTraffic.None);
        var text = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"inbound\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"forwards\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressMovingOnceCutsNothing()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.3.2:40001", 4, 3),
            ("10.99.3.2:40001", 8, 3),
            ("10.99.3.2:40001", 12, 3));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ADeviceGoingBetweenTwoNetworksIsNotCut()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 2, 0),
            ("10.99.1.2:40000", 4, 0),
            ("10.99.2.2:50000", 6, 0),
            ("10.99.1.2:40000", 8, 0));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ADeviceGoingBetweenTwoNetworksThroughItsRekeysIsNotCut()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 20, 0),
            ("10.99.1.2:40000", 40, 0),
            ("10.99.2.2:50000", 130, 120),
            ("10.99.1.2:40000", 150, 120),
            ("10.99.2.2:50000", 250, 240),
            ("10.99.1.2:40000", 270, 240),
            ("10.99.2.2:50000", 370, 360),
            ("10.99.1.2:40000", 390, 360),
            ("10.99.2.2:50000", 490, 480),
            ("10.99.1.2:40000", 510, 480));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ADeviceOnAnInterfaceThatRenewsItsKeysOftenIsNotCut()
    {
        var guard = new PeerGuard();
        var cuts = new List<GuardCut>();

        for (var second = 0; second <= 300; second += 5)
        {
            var point = second / 10 % 2 == 0 ? "10.99.1.2:40000" : "10.99.2.2:50000";
            var device = Device(point, (ulong)second + 1, Start.AddSeconds(second / 25 * 25), 25);
            cuts.AddRange(guard.Observe(device, Quiet, Start.AddSeconds(second)));
        }

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ASecondDeviceIsCaughtOnAnInterfaceThatRenewsItsKeysOften()
    {
        var guard = new PeerGuard();
        var cuts = new List<GuardCut>();

        foreach (var (point, seconds, shake) in TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0))
        {
            var device = Device(point, (ulong)(seconds * 10) + 1, Start.AddSeconds(shake), 25);
            cuts.AddRange(guard.Observe(device, Quiet, Start.AddSeconds(seconds)));
        }

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
    }

    [Fact]
    public void ASecondDeviceComingAfterTheScheduleIsStillTheSecond()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 200, 200),
            ("10.99.1.2:40000", 216, 215),
            ("10.99.2.2:50000", 230, 230),
            ("10.99.1.2:40000", 246, 245));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal([IPEndPoint.Parse("10.99.2.2:50000")], cut.Cut);
    }

    [Fact]
    public void OneRoundOfHandshakesCutsNothing()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, ("10.99.1.2:40000", 0, 0), ("10.99.2.2:50000", 10, 10), ("10.99.1.2:40000", 26, 25));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ASecondDeviceIsCaughtAfterTwoRoundsOfHandshakes()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 10, 10),
            ("10.99.1.2:40000", 12, 10),
            ("10.99.2.2:50000", 14, 10),
            ("10.99.1.2:40000", 26, 25),
            ("10.99.2.2:50000", 28, 25),
            ("10.99.2.2:50000", 40, 40),
            ("10.99.1.2:40000", 42, 40),
            ("10.99.1.2:40000", 56, 55));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal([IPEndPoint.Parse("10.99.2.2:50000")], cut.Cut);
        Assert.Equal([new GuardHold(IPEndPoint.Parse("10.99.2.2:50000"), 51820)], guard.Holds());
        Assert.Equal(["10.99.2.2:50000"], guard.Cut("awg1", Key));
    }

    [Fact]
    public void RoundsFartherApartThanTheMemoryCutNothing()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 100, 100),
            ("10.99.1.2:40000", 200, 200),
            ("10.99.2.2:50000", 300, 300),
            ("10.99.1.2:40000", 400, 400));

        Assert.Empty(cuts);
    }

    [Fact]
    public void TheCutIsLiftedWhenTheKeptDeviceFallsSilent()
    {
        var guard = new PeerGuard();
        Watch(guard, TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0));

        guard.Observe(Device("10.99.1.2:40000", 1000, Start.AddSeconds(55)), Quiet, Start.AddSeconds(70));
        var held = guard.Holds();
        guard.Observe(Device("10.99.1.2:40000", 1000, Start.AddSeconds(55)), Quiet, Start.AddSeconds(150));

        Assert.Single(held);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void AThirdAddressComingBetweenIsCutToo()
    {
        var guard = new PeerGuard();
        Watch(guard, TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0));

        var cuts = Watch(guard, TwoRounds("10.99.1.2:40000", "10.99.4.2:60000", 60)[1..]);

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal(2, guard.Holds().Count);
    }

    [Fact]
    public void ADeviceTakingAnotherPortKeepsItsTurns()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 10, 10),
            ("10.99.1.2:40001", 26, 25),
            ("10.99.2.2:50000", 40, 40),
            ("10.99.1.2:40002", 56, 55));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40002"), cut.First);
        Assert.Equal([new GuardHold(IPEndPoint.Parse("10.99.2.2:50000"), 51820)], guard.Holds());
    }

    [Fact]
    public void ADeviceRaisingItsSessionAgainOnOneNetworkIsNotCut()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0, 0),
            ("10.99.2.2:50000", 10, 10),
            ("10.99.1.2:40001", 26, 25),
            ("10.99.1.2:40002", 40, 40),
            ("10.99.1.2:40003", 56, 55));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void TwoDevicesBehindOneRouterAreToldApartByThePort()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, TwoRounds("10.99.1.2:40000", "10.99.1.2:50000", 0));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal([new GuardHold(IPEndPoint.Parse("10.99.1.2:50000"), 51820)], guard.Holds());
    }

    [Fact]
    public void TheFrontOfWebSocketIsNotCut()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, TwoRounds("127.0.0.1:41000", "127.0.0.1:42000", 0));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ASecondDeviceIsCutWhileTheFirstComesThroughTheFront()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, TwoRounds("127.0.0.1:41000", "10.99.2.2:50000", 0));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("127.0.0.1:41000"), cut.First);
        Assert.Equal([new GuardHold(IPEndPoint.Parse("10.99.2.2:50000"), 51820)], guard.Holds());
    }

    [Fact]
    public void TheGuardHearsAPeerByItsCounter()
    {
        var guard = new PeerGuard();

        guard.Observe(Device("10.99.1.2:40000", 100, Start), Quiet, Start.AddSeconds(10));
        var first = guard.Heard("awg1", Key);
        guard.Observe(Device("10.99.1.2:40000", 100, Start), Quiet, Start.AddSeconds(20));
        var same = guard.Heard("awg1", Key);
        guard.Observe(Device("10.99.1.2:40000", 132, Start), Quiet, Start.AddSeconds(30));

        Assert.Equal(Start, first);
        Assert.Equal(Start, same);
        Assert.Equal(Start.AddSeconds(30), guard.Heard("awg1", Key));
        Assert.Null(guard.Heard("awg1", "other"));
    }

    [Fact]
    public void TheTimeAClientCountsOnlineTakesTwoKeepalivesAtLeast()
    {
        var fresh = ConfigDefaults.Fresh("awg1");

        Assert.Equal(60, fresh.OfflineAfter);
        Assert.Null(ConfigRules.Check(fresh));
        Assert.Equal("bad-offline-after", ConfigRules.Check(fresh with { OfflineAfter = 40 })?.Code);
        Assert.Equal("bad-offline-after", ConfigRules.Check(fresh with { Keepalive = 0, OfflineAfter = 5 })?.Code);
        Assert.Equal("bad-offline-after", ConfigRules.Check(fresh with { OfflineAfter = 4000 })?.Code);
        Assert.Null(ConfigRules.Check(fresh with { Keepalive = 0, OfflineAfter = 10 }));
    }

    [Fact]
    public async Task TheTimeAClientCountsOnlineIsKeptWithTheEndpoint()
    {
        using var bench = new Bench();

        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { OfflineAfter = 90 }, CancellationToken.None);
        var read = await bench.Configs.FindAsync(added.Record!.Id, CancellationToken.None);

        Assert.Equal(90, read!.OfflineAfter);
    }

    [Fact]
    public void TheRulesetHoldsTheAddressesOffTheirPorts()
    {
        var text = GuardRuleset.Text(
        [
            new GuardHold(IPEndPoint.Parse("10.99.2.2:50000"), 51820),
            new GuardHold(IPEndPoint.Parse("[fd00::2]:5000"), 443),
        ]);

        Assert.Contains("delete table inet amneziageo_guard", text, StringComparison.Ordinal);
        Assert.Contains("elements = { 10.99.2.2 . 50000 . 51820 timeout 180s }", text, StringComparison.Ordinal);
        Assert.Contains("elements = { fd00::2 . 5000 . 443 timeout 180s }", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRulesetHoldsNothingOff()
    {
        Assert.DoesNotContain("elements", GuardRuleset.Text([]), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardIsOffUnlessItIsAskedFor()
    {
        Assert.False(new GuardOptions().IsEnabled);
    }

    [Fact]
    public async Task AGuardThatIsOffCutsNoSecondDevice()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        var kernel = new Kernel();
        var ledger = new Ledger();
        var guard = Guard(bench, kernel, ledger, new GuardOptions());

        await WatchAsync(bench, guard, kernel, TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0));

        Assert.Empty(kernel.Updates);
        Assert.Empty(guard.Cut("awg1", Key));
        Assert.DoesNotContain("elements", ledger.Ruleset, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGuardThatIsOffTakesItsTableOffTheFirewallOnce()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        var kernel = new Kernel();
        var ledger = new Ledger();
        var guard = Guard(bench, kernel, ledger, new GuardOptions { IsEnabled = false });

        await WatchAsync(bench, guard, kernel, TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0));

        Assert.Contains("delete table inet amneziageo_guard", ledger.Ruleset, StringComparison.Ordinal);
        Assert.DoesNotContain("chain", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Single(ledger.Steps, step => step == "firewall");
    }

    [Fact]
    public async Task AGuardThatIsOffStillHearsWhoIsOnline()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        var kernel = new Kernel();
        var guard = Guard(bench, kernel, new Ledger(), new GuardOptions { IsEnabled = false });

        await WatchAsync(bench, guard, kernel, ("10.99.1.2:40000", 0, 0), ("10.99.1.2:40000", 30, 0));
        var online = guard.Online(added.Record!, Key);
        bench.Clock.Pass(TimeSpan.FromSeconds(61));

        Assert.True(online);
        Assert.False(guard.Online(added.Record!, Key));
    }

    [Fact]
    public async Task AGuardThatIsOnCutsTheSecondDeviceAndLaysThePeerAnew()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        var kernel = new Kernel();
        var ledger = new Ledger();
        var guard = Guard(bench, kernel, ledger, new GuardOptions { IsEnabled = true });

        await WatchAsync(bench, guard, kernel, TwoRounds("10.99.1.2:40000", "10.99.2.2:50000", 0));

        Assert.Equal(["10.99.2.2:50000"], guard.Cut("awg1", Key));
        Assert.Contains("10.99.2.2 . 50000 . 51820", ledger.Ruleset, StringComparison.Ordinal);
        Assert.Equal(2, kernel.Updates.Count);
        Assert.True(kernel.Updates[0].Peers[0].Remove);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), kernel.Updates[1].Peers[0].Endpoint);
    }

    [Fact]
    public void ASecondDeviceIsRefusedWhileTheFirstHoldsTheConfiguration()
    {
        var holds = new DeviceHolds();

        var first = holds.Take(Key, "phone", Start);
        var second = holds.Take(Key, "laptop", Start.AddSeconds(30));
        var again = holds.Take(Key, "phone", Start.AddSeconds(60));
        var late = holds.Take(Key, "laptop", Start.AddSeconds(61) + DeviceHolds.Lease);

        Assert.True(first.IsHeld);
        Assert.False(second.IsHeld);
        Assert.Equal(Start, second.Since);
        Assert.True(again.IsHeld);
        Assert.Equal(Start, again.Since);
        Assert.True(late.IsHeld);
    }

    [Fact]
    public void OnlyTheHoldingDeviceLetsTheConfigurationGo()
    {
        var holds = new DeviceHolds();
        holds.Take(Key, "phone", Start);

        holds.Drop(Key, "laptop");
        var kept = holds.Take(Key, "laptop", Start.AddSeconds(5));
        holds.Drop(Key, "phone");
        var free = holds.Take(Key, "laptop", Start.AddSeconds(6));

        Assert.False(kept.IsHeld);
        Assert.True(free.IsHeld);
    }

    private static (string Point, int Seconds, int Shake)[] TwoRounds(string first, string second, int from) =>
    [
        (first, from, from),
        (second, from + 10, from + 10),
        (first, from + 26, from + 25),
        (second, from + 40, from + 40),
        (first, from + 56, from + 55),
    ];

    private static List<GuardCut> Watch(PeerGuard guard, params (string Point, int Seconds, int Shake)[] steps)
    {
        var cuts = new List<GuardCut>();
        foreach (var (point, seconds, shake) in steps)
        {
            var device = Device(point, (ulong)(seconds * 10) + 1, Start.AddSeconds(shake));
            cuts.AddRange(guard.Observe(device, Quiet, Start.AddSeconds(seconds)));
        }

        return cuts;
    }

    private static ClientGuard Guard(Bench bench, Kernel kernel, Ledger ledger, GuardOptions options) =>
        new(bench.Scopes, kernel, ledger, options, bench.Clock, NullLogger<ClientGuard>.Instance);

    private static async Task WatchAsync(
        Bench bench,
        ClientGuard guard,
        Kernel kernel,
        params (string Point, int Seconds, int Shake)[] steps)
    {
        foreach (var (point, seconds, shake) in steps)
        {
            kernel.Hold(Device(point, (ulong)(seconds * 10) + 1, Start.AddSeconds(shake)));
            bench.Clock.Now = Start.AddSeconds(seconds);
            await guard.LookAsync(CancellationToken.None);
        }
    }

    private static AwgDevice Device(string point, ulong rx, DateTimeOffset? handshake = null, uint rekey = 0) => new()
    {
        Name = "awg1",
        ListenPort = 51820,
        Obfuscation = new AwgObfuscation { RekeyAfterTime = new AwgRange(rekey) },
        Peers = [new AwgPeer { PublicKey = Key, Endpoint = IPEndPoint.Parse(point), RxBytes = rx, LastHandshake = handshake }],
    };
}
