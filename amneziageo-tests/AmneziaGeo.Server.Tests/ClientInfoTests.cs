using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Traffic;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientInfoTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheSummaryUsesTheSameEffectiveValuesAsTheExport()
    {
        var config = Endpoint() with { PrivateKey = "interface-private", PresharedKey = "interface-psk" };
        var client = Member() with { PrivateKey = "client-private", PresharedKey = "client-psk", SubscriptionId = "subscription-secret" };
        var template = new ClientTemplate
        {
            Id = 4, Name = "template", AllowedIps = ["192.0.2.0/24"], Mtu = 1300, Keepalive = 15,
            DnsMode = DnsChoice.Own, Dns = ["9.9.9.9"], PresetsOnly = true, ProxyIds = [2, 5],
        };
        var dns = ClientText.Dns(config, client, template);

        var info = ClientInformation.Configuration(config, client, template, dns, [new ClientInfoReference(1, "routes")]);
        var export = ClientText.Text(config, client, template);

        Assert.Equal(client.Address, info.Address);
        Assert.Equal(ClientText.Endpoint(config), info.Endpoint);
        Assert.Equal(dns.Servers, info.Dns);
        Assert.Equal("template", info.DnsSource);
        Assert.Equal("template", info.AllowedIpsSource);
        Assert.Equal("template", info.MtuSource);
        Assert.Equal("template", info.KeepaliveSource);
        Assert.Contains($"MTU = {info.Mtu}", export);
        Assert.Contains($"PersistentKeepalive = {info.Keepalive}", export);
        Assert.Contains($"AllowedIPs = {string.Join(", ", info.AllowedIps)}", export);
        Assert.Equal([2L, 5L], info.ProxyIds);
        Assert.True(info.PresetsOnly);
        var json = JsonSerializer.Serialize(info);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("psk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subscription-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyInterfaceRangesDoNotPretendToUseTemplateDefaults()
    {
        var config = Endpoint() with { AllowedIps = [] };
        var client = Member();
        var noTemplate = ClientInformation.Configuration(config, client, null, ClientText.Dns(config, client), []);
        var template = new ClientTemplate { Name = "empty" };
        var inherited = ClientInformation.Configuration(config, client, template, ClientText.Dns(config, client, template), []);

        Assert.Empty(noTemplate.AllowedIps);
        Assert.Equal("interface", noTemplate.AllowedIpsSource);
        Assert.NotEmpty(inherited.AllowedIps);
        Assert.Equal("default", inherited.AllowedIpsSource);
    }

    [Fact]
    public void ZeroTemplateOverridesStayZero()
    {
        var config = Endpoint();
        var client = Member();
        var template = new ClientTemplate { Name = "zero", Mtu = 0, Keepalive = 0 };

        var info = ClientInformation.Configuration(config, client, template, ClientText.Dns(config, client, template), []);

        Assert.Equal(0, info.Mtu);
        Assert.Equal(0, info.Keepalive);
        Assert.Equal("template", info.MtuSource);
        Assert.Equal("template", info.KeepaliveSource);
        Assert.Equal("interface", info.AllowedIpsSource);
    }

    [Fact]
    public void StandInsUseTheirActualDnsSource()
    {
        var config = Endpoint();
        var client = Member();
        var template = new ClientTemplate { Name = "names", StandIns = true };

        var unavailable = ClientText.Dns(config, client, template);
        var fallback = ClientInformation.Configuration(config, client, template, unavailable, []);
        var available = ClientText.Dns(config, client, template, ["10.8.0.1"], ["10.8.0.1"]);
        var active = ClientInformation.Configuration(config, client, template, available, []);

        Assert.Equal("interface", fallback.AllowedIpsSource);
        Assert.Equal("stand-ins", active.DnsSource);
        Assert.Equal("template", active.AllowedIpsSource);
        Assert.Equal(ClientText.Routed(config, client, template, available), active.AllowedIps);
    }

    [Fact]
    public void LongRoutingListsAreBoundedWithoutChangingTheirTotal()
    {
        var config = Endpoint() with { AllowedIps = [.. Enumerable.Range(0, 1501).Select(value => $"10.{value / 256}.{value % 256}.0/24")] };
        var client = Member();

        var info = ClientInformation.Configuration(config, client, null, ClientText.Dns(config, client), []);

        Assert.Equal(1000, info.AllowedIps.Count);
        Assert.Equal(1501, info.AllowedIpsTotal);
        Assert.True(info.AllowedIpsTruncated);
        Assert.Equal(config.AllowedIps.Take(1000), info.AllowedIps);
    }

    [Fact]
    public void TheLegacyDailyQuotaKeepsItsClientSourceWhenTheTemplateLimitIsOff()
    {
        var client = Member() with { DailyLimit = 500, TemplateId = 4 };
        var template = new ClientTemplate { Id = 4, Name = "off", Limits = new TrafficLimits(Enabled: false) };

        var limits = ClientInformation.Limits(client, template, null);

        Assert.Null(limits.Policy);
        Assert.Equal("client", limits.Source);
        Assert.Null(limits.TemplateId);
        Assert.Equal(500, limits.LegacyDailyLimit);
    }

    [Theory]
    [InlineData(false, "template")]
    [InlineData(true, "client")]
    public void TheEffectiveLimitNamesItsActualSource(bool own, string expected)
    {
        var client = Member() with { DailyLimit = 500, TemplateId = 4, OverrideLimits = own, Limits = new TrafficLimits(Enabled: true, QuotaBytes: 100) };
        var template = new ClientTemplate { Id = 4, Name = "limited", Limits = new TrafficLimits(Enabled: true, QuotaBytes: 200) };

        var limits = ClientInformation.Limits(client, template, null);

        Assert.Equal(expected, limits.Source);
        Assert.Equal(own ? 100 : 200, limits.Policy!.QuotaBytes);
        Assert.Equal(0, limits.LegacyDailyLimit);
        Assert.Equal(own ? null : 4L, limits.TemplateId);
    }

    [Fact]
    public void TheConnectionReasonNamesDisabledAndApplicationFailuresPrecisely()
    {
        var client = Member();
        var config = Endpoint();
        var state = ClientState.Missing(client);
        var status = new ClientLimitStatus(new TrafficLimits(Enabled: true), false, null, Noon, Noon.AddDays(1),
            0, 0, 0, null, false, false, true, 0, 0, false, "tc failed");
        var traffic = ClientTraffic.None with { Limits = status, IsSpent = true };

        Assert.Equal("disabled-client", ClientInformation.Reason(client with { IsEnabled = false }, config, state, traffic));
        Assert.Equal("disabled-interface", ClientInformation.Reason(client, config with { IsEnabled = false }, state, traffic));
        Assert.Equal("limits-not-applied", ClientInformation.Reason(client, config, state, traffic));
        Assert.Equal("quota-blocked", ClientInformation.Reason(client, config, state, traffic with { Limits = status with { Applied = true, Error = "" } }));
        Assert.Equal("peer-not-present", ClientInformation.Reason(client, config, state, ClientTraffic.None));
        Assert.Null(ClientInformation.Reason(client, config, state with { IsPresent = true }, ClientTraffic.None));
    }

    [Fact]
    public void TheTrafficMetadataDistinguishesUnknownFromObservedZero()
    {
        var meter = new TrafficMeter(DateOnly.FromDateTime(Noon.UtcDateTime));
        Assert.False(meter.Observation(1).TodayKnown);
        Assert.Null(meter.Observation(1).ObservedUtc);

        meter.Observe([new TrafficReading(1, 0, 0)], Noon);
        Assert.True(meter.Observation(1).TodayKnown);
        Assert.False(meter.Observation(1).RateKnown);
        Assert.Null(meter.Observation(1).LastActivityUtc);

        meter.Observe([new TrafficReading(1, 0, 0)], Noon.AddSeconds(2));
        Assert.True(meter.Observation(1).RateKnown);
        Assert.Equal(default, meter.Rate(1));
        Assert.Null(meter.Observation(1).LastActivityUtc);
    }

    [Fact]
    public void LastActivityTracksByteGrowthAndSurvivesAnAbsentPeer()
    {
        var meter = new TrafficMeter(DateOnly.FromDateTime(Noon.UtcDateTime));
        meter.Observe([new TrafficReading(1, 10, 20)], Noon);
        meter.Observe([new TrafficReading(1, 110, 220)], Noon.AddSeconds(2));
        Assert.Equal(Noon.AddSeconds(2), meter.Observation(1).LastActivityUtc);
        Assert.Equal(new TrafficRate(50, 100), meter.Rate(1));

        meter.Observe([new TrafficReading(1, 110, 220)], Noon.AddSeconds(4));
        Assert.Equal(Noon.AddSeconds(2), meter.Observation(1).LastActivityUtc);
        meter.Observe([], Noon.AddSeconds(6));
        Assert.Null(meter.Observation(1).ObservedUtc);
        Assert.False(meter.Observation(1).RateKnown);
        Assert.Equal(Noon.AddSeconds(2), meter.Observation(1).LastActivityUtc);
        meter.Observe([new TrafficReading(1, 110, 220)], Noon.AddSeconds(8));
        Assert.False(meter.Observation(1).RateKnown);
    }

    [Fact]
    public void RepeatedTimestampsAndCounterResetsDoNotProduceInventedRates()
    {
        var meter = new TrafficMeter(DateOnly.FromDateTime(Noon.UtcDateTime));
        meter.Observe([new TrafficReading(1, 100, 100)], Noon);
        meter.Observe([new TrafficReading(1, 200, 200)], Noon);
        Assert.False(meter.Observation(1).RateKnown);
        Assert.Equal(default, meter.Rate(1));
        meter.Observe([new TrafficReading(1, 5, 7)], Noon.AddSeconds(2));
        Assert.True(meter.Observation(1).RateKnown);
        Assert.Equal(new TrafficRate(2.5, 3.5), meter.Rate(1));
        Assert.Equal(new ClientUsage(105, 107), meter.Used(1));
    }

    [Fact]
    public void OnlyTheCurrentDayLoadedFromStorageCountsAsKnownToday()
    {
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        var meter = new TrafficMeter(day);
        meter.Load([new TrafficDay(1, day.AddDays(-1), new ClientUsage(1, 2), default),
            new TrafficDay(2, day, default, default)]);
        Assert.False(meter.Observation(1).TodayKnown);
        Assert.True(meter.Observation(2).TodayKnown);
        Assert.False(meter.Observation(2).RateKnown);
        meter.Turn(day.AddDays(1));
        Assert.False(meter.Observation(2).TodayKnown);
    }

    [Fact]
    public async Task DailyHistoryIsBoundedAndBelongsToOneClient()
    {
        using var bench = new Bench();
        var config = (await bench.Configs.AddAsync(Endpoint(), CancellationToken.None).ConfigureAwait(false)).Record!;
        var first = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "one") with { Address = ["10.8.0.2/32"] }, CancellationToken.None).ConfigureAwait(false)).Record!;
        var second = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "two") with { Address = ["10.8.0.3/32"] }, CancellationToken.None).ConfigureAwait(false)).Record!;
        var store = new TrafficStore(bench.Db);
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        await store.SaveAsync([new TrafficDay(first.Id, day, default, default),
            new TrafficDay(first.Id, day.AddDays(-1), new ClientUsage(5, 7), default),
            new TrafficDay(first.Id, day.AddDays(-30), new ClientUsage(50, 70), default),
            new TrafficDay(second.Id, day, new ClientUsage(500, 700), default)], CancellationToken.None).ConfigureAwait(false);

        var history = await store.DaysAsync(first.Id, day.AddDays(-29), day, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(2, history.Count);
        Assert.All(history, row => Assert.Equal(first.Id, row.ClientId));
        Assert.Equal(new ClientUsage(5, 7), history[0].Used);
        Assert.Equal(default, history[1].Used);
        Assert.Empty(await store.DaysAsync(second.Id, day.AddDays(-7), day.AddDays(-1), CancellationToken.None).ConfigureAwait(false));
    }

    [Fact]
    public void AccessStatisticsUseTheCurrentBindingAfterCreationAndSurviveRenames()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ag806-access-" + Guid.NewGuid().ToString("N"));
        var records = new AccessRecords(Path.Combine(folder, AccessDefaults.FileName));
        records.Prepare();
        try
        {
            var client = Member() with { CreatedUtc = Noon, Name = "new name", Address = ["10.8.0.2/32", "fd00::2/128"] };
            var config = Endpoint();
            records.Write([
                Record(Noon.AddSeconds(1), "10.8.0.2", config.Name, "new name"),
                Record(Noon.AddSeconds(2), "fd00::2", config.Name, "old name"),
                Record(Noon.AddSeconds(3), "10.8.0.3", config.Name, "new name"),
                Record(Noon.AddSeconds(4), "10.8.0.2", "awg-other", "new name"),
                Record(Noon.AddSeconds(-1), "10.8.0.2", config.Name, "previous owner"),
            ]);

            var query = ClientInformation.Access(client, config, Noon.AddDays(-1), Noon.AddHours(1));
            var summary = records.Summary(query, AccessGrouping.Client);

            Assert.Equal(2, summary.Total);
            Assert.Equal(2, records.List(query).Count);
            Assert.Equal(Noon, query.From);
            Assert.Empty(records.List(ClientInformation.Access(client with { Address = [] }, config, Noon, Noon.AddHours(1))));
            Assert.Empty(records.List(ClientInformation.Access(client, null, Noon, Noon.AddHours(1))));
        }
        finally
        {
            Pools.Clear(records.Location);
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void AReassignedAddressDoesNotBringThePreviousOwnersHistory()
    {
        var client = Member() with { CreatedUtc = Noon.AddDays(-10), UpdatedUtc = Noon };
        var config = Endpoint() with { UpdatedUtc = Noon.AddDays(-2) };
        var query = ClientInformation.Access(client, config, Noon.AddDays(-7), Noon.AddHours(1));
        Assert.Equal(Noon, query.From);
        Assert.Equal(Noon, ClientInformation.BindingSince(client, config));
        var changedInterface = config with { UpdatedUtc = Noon.AddMinutes(1) };
        Assert.Equal(Noon.AddMinutes(1), ClientInformation.Access(client, changedInterface, Noon.AddDays(-7), Noon.AddHours(1)).From);
    }

    [Fact]
    public void TheDefaultStatisticsPeriodDoesNotUnderflowTheDateRange()
    {
        Assert.Equal(DateTimeOffset.MinValue, ClientInformation.PeriodStart(null, DateTimeOffset.MinValue));
        Assert.Equal(Noon.AddHours(-1), ClientInformation.PeriodStart(null, Noon));
        Assert.Equal(Noon.AddDays(-1), ClientInformation.PeriodStart(Noon.AddDays(-1), Noon));
        var shifted = new DateTimeOffset(1, 1, 1, 1, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(shifted, ClientInformation.PeriodStart(null, shifted));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void OnlyRecentPastSamplesAreFresh(int ageSeconds, bool expected)
    {
        Assert.Equal(expected, ClientInformation.Recent(Noon.AddSeconds(-ageSeconds), Noon));
        Assert.False(ClientInformation.Recent(null, Noon));
    }

    [Fact]
    public async Task AnAtomicTrafficSnapshotKeepsItsAccountingDayDuringConcurrentTurns()
    {
        var clock = new Clock(Noon);
        var ledger = new TrafficLedger(clock);
        var client = Member();
        var day = DateOnly.FromDateTime(Noon.UtcDateTime);
        ledger.Observe([client], [new TrafficReading(client.Id, 0, 0)], Noon);
        var first = ledger.Snapshot(client);
        var pending = ledger.Pending().ToArray();
        var reading = ledger.Snapshot(client);
        Assert.Equal(pending, ledger.Pending());
        Assert.Equal(first, reading);
        using var start = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            start.Wait();
            for (var index = 1; index <= 1000; index++)
            {
                ledger.Turn(day.AddDays(index));
                ledger.Observe([client], [new TrafficReading(client.Id, (ulong)index * 10, (ulong)index * 20)], Noon.AddDays(index));
            }
        });
        start.Set();
        do
        {
            var snapshot = ledger.Snapshot(client);
            if (snapshot.Observation.TodayKnown && snapshot.Observation.LastActivityUtc is { } activity)
            {
                Assert.Equal(snapshot.Day, DateOnly.FromDateTime(activity.UtcDateTime));
                Assert.Equal(new ClientUsage(10, 20), snapshot.Traffic.Used);
            }
            else if (!snapshot.Observation.TodayKnown)
            {
                Assert.Equal(default, snapshot.Traffic.Used);
            }
        }
        while (!writer.IsCompleted);
        await writer.ConfigureAwait(false);
        Assert.Equal(day, first.Day);
        Assert.Equal(default, first.Traffic.Used);
        Assert.Equal(day.AddDays(1000), ledger.Snapshot(client).Day);
    }

    private static AccessRecord Record(DateTimeOffset at, string source, string inbound, string client) => new()
    {
        At = at, Source = source, Inbound = inbound, Client = client, Name = "example.test", Target = "192.0.2.1", Port = 443,
    };

    private static ServerConfig Endpoint() => ConfigDefaults.Fresh("awg1") with
    {
        Host = "vpn.example.test", ListenPort = 51820, Address = ["10.8.0.1/24"],
        Dns = ["1.1.1.1"], AllowedIps = ["0.0.0.0/0"], Mtu = 1420, Keepalive = 25,
    };

    private static TunnelClient Member() => new() { Id = 1, Name = "member", Address = ["10.8.0.2/32"], IsEnabled = true };
}
