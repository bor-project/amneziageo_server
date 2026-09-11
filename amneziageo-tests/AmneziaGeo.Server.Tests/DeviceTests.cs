using System.Net;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Guard;

namespace AmneziaGeo.Server.Tests;

public class DeviceTests
{
    private const string Key = "eyf4cmoVSwDUFfS+15aWOaz3jYFQ1pPraV/WkHu3zDk=";

    private static readonly DateTimeOffset Start = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ADeviceWaitsForSeveralDevicesToBeTurnedOn()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: false);

        var added = await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None);

        Assert.Equal("client-single-device", added.Code);
    }

    [Fact]
    public async Task ADeviceTakesKeysAnAddressAndASubscriptionOfItsOwn()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: true);

        var added = await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None);

        Assert.True(added.IsOk, added.Message);
        var device = added.Record!;
        Assert.Equal("milena-2", device.Name);
        Assert.Equal(parent.Id, device.ParentId);
        Assert.Equal(parent.ConfigId, device.ConfigId);
        Assert.NotEqual(parent.PublicKey, device.PublicKey);
        Assert.NotEqual(parent.SubscriptionId, device.SubscriptionId);
        Assert.Equal(["10.8.0.3/32"], device.Address);
        Assert.False(device.MultiDevice);
    }

    [Fact]
    public async Task ADeviceTakesNoDevicesOfItsOwn()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: true);
        var device = (await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None)).Record!;

        var again = await bench.Clients.AddDeviceAsync(device.Id, CancellationToken.None);

        Assert.Equal("client-is-device", again.Code);
    }

    [Fact]
    public async Task TheDevicesFollowTheClientOffAndOn()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: true);
        var device = (await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None)).Record!;

        await bench.Clients.SwitchAsync(parent.Id, false, CancellationToken.None);
        var off = await bench.Clients.FindAsync(device.Id, CancellationToken.None);
        await bench.Clients.SwitchAsync(parent.Id, true, CancellationToken.None);
        var on = await bench.Clients.FindAsync(device.Id, CancellationToken.None);

        Assert.False(off!.IsEnabled);
        Assert.True(on!.IsEnabled);
    }

    [Fact]
    public async Task SeveralDevicesStayOnWhileTheClientCarriesDevices()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: true);
        var device = (await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None)).Record!;

        var refused = await bench.Clients.ChangeAsync(parent.Id, parent with { MultiDevice = false }, CancellationToken.None);
        await bench.Clients.RemoveAsync(device.Id, CancellationToken.None);
        var taken = await bench.Clients.ChangeAsync(parent.Id, parent with { MultiDevice = false }, CancellationToken.None);

        Assert.Equal("client-has-devices", refused.Code);
        Assert.True(taken.IsOk, taken.Message);
        Assert.False(taken.Record!.MultiDevice);
    }

    [Fact]
    public async Task RemovingTheClientRemovesItsDevices()
    {
        using var bench = new Bench();
        var parent = await ParentAsync(bench, multiDevice: true);
        await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None);
        await bench.Clients.AddDeviceAsync(parent.Id, CancellationToken.None);

        var devices = await bench.Clients.DevicesAsync(parent.Id, CancellationToken.None);
        await bench.Clients.RemoveAsync(parent.Id, CancellationToken.None);

        Assert.Equal(["milena-2", "milena-3"], devices.Select(one => one.Name));
        Assert.Empty(await bench.Clients.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void AnAddressMovingOnceCutsNothing()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, ("10.99.1.2:40000", 0), ("10.99.3.2:40001", 4), ("10.99.3.2:40001", 8), ("10.99.3.2:40001", 12));

        Assert.Empty(cuts);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void ASecondDeviceIsCaughtOnceTheFirstSpeaksAgain()
    {
        var guard = new PeerGuard();

        var cuts = Watch(
            guard,
            ("10.99.1.2:40000", 0),
            ("10.99.2.2:50000", 2),
            ("10.99.1.2:40000", 4),
            ("10.99.2.2:50000", 6),
            ("10.99.1.2:40000", 8));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal([IPEndPoint.Parse("10.99.2.2:50000")], cut.Cut);
        Assert.Equal([new GuardHold(IPEndPoint.Parse("10.99.2.2:50000"), 51820)], guard.Holds());
        Assert.Equal(["10.99.2.2:50000"], guard.Cut("awg1", Key));
    }

    [Fact]
    public void AnAddressComingBackAfterTheSilenceCutsNothing()
    {
        var guard = new PeerGuard();

        var cuts = Watch(guard, ("10.99.1.2:40000", 0), ("10.99.2.2:50000", 2), ("10.99.1.2:40000", 70));

        Assert.Empty(cuts);
    }

    [Fact]
    public void TheCutIsLiftedWhenTheKeptDeviceFallsSilent()
    {
        var guard = new PeerGuard();
        Watch(guard, ("10.99.1.2:40000", 0), ("10.99.2.2:50000", 2), ("10.99.1.2:40000", 4));

        guard.Observe(Device("10.99.1.2:40000", 1000), Quiet, Start.AddSeconds(30));
        var held = guard.Holds();
        guard.Observe(Device("10.99.1.2:40000", 1000), Quiet, Start.AddSeconds(100));

        Assert.Single(held);
        Assert.Empty(guard.Holds());
    }

    [Fact]
    public void AThirdAddressComingBetweenIsCutToo()
    {
        var guard = new PeerGuard();
        Watch(guard, ("10.99.1.2:40000", 0), ("10.99.2.2:50000", 2), ("10.99.1.2:40000", 4));

        var cuts = Watch(guard, ("10.99.4.2:60000", 10), ("10.99.1.2:40000", 12), ("10.99.4.2:60000", 14), ("10.99.1.2:40000", 16));

        var cut = Assert.Single(cuts);
        Assert.Equal(IPEndPoint.Parse("10.99.1.2:40000"), cut.First);
        Assert.Equal(2, guard.Holds().Count);
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

    private static List<GuardCut> Watch(PeerGuard guard, params (string Point, int Seconds)[] steps)
    {
        var cuts = new List<GuardCut>();
        foreach (var (point, seconds) in steps)
        {
            cuts.AddRange(guard.Observe(Device(point, (ulong)(seconds * 10) + 1), Quiet, Start.AddSeconds(seconds)));
        }

        return cuts;
    }

    private static AwgDevice Device(string point, ulong rx, DateTimeOffset? handshake = null) => new()
    {
        Name = "awg1",
        ListenPort = 51820,
        Peers = [new AwgPeer { PublicKey = Key, Endpoint = IPEndPoint.Parse(point), RxBytes = rx, LastHandshake = handshake }],
    };

    private static async Task<TunnelClient> ParentAsync(Bench bench, bool multiDevice)
    {
        var endpoint = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None);
        var added = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "milena") with { Address = ["10.8.0.2/32"], MultiDevice = multiDevice },
            CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        return added.Record!;
    }
}
