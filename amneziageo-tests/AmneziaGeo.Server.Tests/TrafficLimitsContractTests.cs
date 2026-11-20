using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public class TrafficLimitsContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AClientInheritsTheTemplateUnlessItOverridesTheWholePolicy()
    {
        var inherited = Policy();
        var own = Policy() with { UploadBitsPerSecond = 5_000_000 };
        var template = TemplateDefaults.Fresh() with { Limits = inherited };
        var client = ClientDefaults.Fresh(1, "quota") with { Limits = own, DailyLimit = 123 };

        Assert.Equal(inherited, TrafficLimitPolicy.Resolve(client, template));
        Assert.Equal(own, TrafficLimitPolicy.Resolve(client with { OverrideLimits = true }, template));
        Assert.Null(TrafficLimitPolicy.Resolve(client, null));
        Assert.Null(TrafficLimitPolicy.Resolve(client with { OverrideLimits = true, Limits = null }, template));
        Assert.Null(TrafficLimitPolicy.Resolve(
            client with { OverrideLimits = true, Limits = own with { Enabled = false } }, template));
    }

    [Fact]
    public void AnEnabledUnlimitedOverrideTakesThePlaceOfTemplateLimits()
    {
        var template = TemplateDefaults.Fresh() with { Limits = Policy() };
        var unlimited = new TrafficLimits(Enabled: true);
        var client = ClientDefaults.Fresh(1, "quota") with { OverrideLimits = true, Limits = unlimited };

        Assert.Equal(unlimited, TrafficLimitPolicy.Resolve(client, template));
        Assert.Equal(TrafficLimitPeriod.Month, unlimited.Period);
        Assert.Equal(TrafficQuotaAction.Throttle, unlimited.QuotaAction);
        Assert.Null(TrafficLimitsRules.Check(unlimited));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1L)]
    [InlineData(7L)]
    [InlineData(1_000_000_000_001L)]
    public void EverySpeedFieldRejectsValuesOutsideItsRange(long value)
    {
        var limits = Policy();
        var invalid = new[]
        {
            limits with { UploadBitsPerSecond = value },
            limits with { DownloadBitsPerSecond = value },
            limits with { ExceededUploadBitsPerSecond = value },
            limits with { ExceededDownloadBitsPerSecond = value },
        };

        foreach (var item in invalid)
        {
            Assert.Equal("bad-traffic-rate", TrafficLimitsRules.Check(item)?.Code);
            Assert.Equal("bad-traffic-rate", TrafficLimitsRules.Check(item with { Enabled = false })?.Code);
        }
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1_125_899_906_842_625L)]
    public void AQuotaOutsideItsRangeIsRefused(long value)
    {
        Assert.Equal("bad-traffic-quota", TrafficLimitsRules.Check(Policy() with { QuotaBytes = value })?.Code);
    }

    [Fact]
    public void TheBoundaryValuesAndEveryCalendarPeriodAreAccepted()
    {
        foreach (var period in new[] { TrafficLimitPeriod.Day, TrafficLimitPeriod.Week, TrafficLimitPeriod.Month })
        {
            var limits = Policy() with
            {
                UploadBitsPerSecond = TrafficLimitsRules.MaxBitsPerSecond,
                DownloadBitsPerSecond = TrafficLimitsRules.MaxBitsPerSecond,
                QuotaBytes = TrafficLimitsRules.MaxQuotaBytes,
                Period = period,
            };

            Assert.Null(TrafficLimitsRules.Check(limits));
        }

        Assert.Null(TrafficLimitsRules.Check(Policy() with
        {
            UploadBitsPerSecond = TrafficLimitsRules.MinBitsPerSecond,
            DownloadBitsPerSecond = TrafficLimitsRules.MinBitsPerSecond,
            ExceededUploadBitsPerSecond = TrafficLimitsRules.MinBitsPerSecond,
            ExceededDownloadBitsPerSecond = TrafficLimitsRules.MinBitsPerSecond,
        }));
        Assert.Null(TrafficLimitsRules.Check(null));
        Assert.Null(TrafficLimitsRules.Check(new TrafficLimits()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("year")]
    [InlineData("Month")]
    [InlineData(null)]
    public void AnUnknownPeriodIsRefused(string? period)
    {
        Assert.Equal("bad-traffic-period", TrafficLimitsRules.Check(Policy() with { Period = period! })?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("drop")]
    [InlineData(null)]
    public void AnUnknownQuotaActionIsRefused(string? action)
    {
        Assert.Equal("bad-traffic-quota-action", TrafficLimitsRules.Check(Policy() with { QuotaAction = action! })?.Code);
    }

    [Fact]
    public void OnlyAnEnabledThrottleQuotaRequiresBothReducedSpeeds()
    {
        var limits = Policy() with { ExceededUploadBitsPerSecond = 0 };

        Assert.Equal("bad-traffic-exceeded-rate", TrafficLimitsRules.Check(limits)?.Code);
        Assert.Equal("bad-traffic-exceeded-rate",
            TrafficLimitsRules.Check(Policy() with { ExceededDownloadBitsPerSecond = 0 })?.Code);
        Assert.Null(TrafficLimitsRules.Check(limits with { Enabled = false }));
        Assert.Null(TrafficLimitsRules.Check(limits with { QuotaBytes = 0 }));
        Assert.Null(TrafficLimitsRules.Check(limits with
        {
            QuotaAction = TrafficQuotaAction.Block,
            ExceededDownloadBitsPerSecond = 0,
        }));
    }

    [Fact]
    public void OmittedAndNullClientLimitsKeepThePolicyAndLegacyDailyLimit()
    {
        var held = ClientDefaults.Fresh(1, "quota") with
        {
            DailyLimit = 123,
            OverrideLimits = true,
            Limits = Policy(),
        };
        foreach (var json in new[]
        {
            """{"configId":1,"name":"quota"}""",
            """{"configId":1,"name":"quota","limits":null,"overrideLimits":null}""",
        })
        {
            var request = JsonSerializer.Deserialize<ClientRequest>(json, Web)!;
            var draft = ClientAnswers.Draft(request, held);

            Assert.Equal(held.Limits, draft.Limits);
            Assert.True(draft.OverrideLimits);
            Assert.Equal(held.DailyLimit, draft.DailyLimit);
        }

        var disabled = new TrafficLimits();
        var replacement = ClientAnswers.Draft(
            new ClientRequest(1, "quota", null, null, null, null, null, null,
                Limits: disabled, OverrideLimits: false), held);

        Assert.Equal(disabled, replacement.Limits);
        Assert.False(replacement.OverrideLimits);
        Assert.Equal(123, replacement.DailyLimit);
    }

    [Fact]
    public void ATemplateKeepsOmittedLimitsAndAcceptsExplicitDisabling()
    {
        var held = TemplateDefaults.Fresh() with { Limits = Policy() };
        var request = new TemplateRequest(held.Name, held.Entries, held.Dns, held.Mtu, held.Keepalive);
        var disabled = new TrafficLimits();

        Assert.Equal(held.Limits, TemplateAnswers.Change(request, held).Limits);
        Assert.Equal(disabled, TemplateAnswers.Change(request with { Limits = disabled }, held).Limits);
        Assert.Null(TemplateAnswers.Draft(request).Limits);
        Assert.Equal(held.Limits, TemplateAnswers.Template(held, []).Limits);
    }

    [Fact]
    public async Task ClientAndTemplatePoliciesRoundTripIndependently()
    {
        using var bench = new Bench();
        var template = await bench.Templates.AddAsync(
            TemplateDefaults.Fresh() with { Name = "Limited", Limits = Policy() },
            CancellationToken.None).ConfigureAwait(false);
        var own = Policy() with { QuotaAction = TrafficQuotaAction.Block };
        var client = await AddClientAsync(bench, template.Record!.Id, own).ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();

        var saved = await bench.Clients.FindAsync(client.Id, CancellationToken.None).ConfigureAwait(false);
        var savedTemplate = await bench.Templates.FindAsync(template.Record.Id, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(own, saved!.Limits);
        Assert.True(saved.OverrideLimits);
        Assert.Equal(123, saved.DailyLimit);
        Assert.Equal(Policy(), savedTemplate!.Limits);
        Assert.Equal(own, TrafficLimitPolicy.Resolve(saved, savedTemplate));

        var disabled = new TrafficLimits();
        var changed = await bench.Clients.ChangeAsync(
            saved.Id, saved with { Limits = disabled }, CancellationToken.None).ConfigureAwait(false);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(disabled, (await bench.Clients.FindAsync(saved.Id, CancellationToken.None).ConfigureAwait(false))!.Limits);
        Assert.Null(TrafficLimitPolicy.Resolve(changed.Record!, savedTemplate));
    }

    [Fact]
    public async Task ClientAndTemplateStoresRejectInvalidPolicies()
    {
        using var bench = new Bench();
        var template = await bench.Templates.AddAsync(
            TemplateDefaults.Fresh() with { Name = "Invalid", Limits = Policy() with { Period = "year" } },
            CancellationToken.None).ConfigureAwait(false);
        var endpoint = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false);
        var client = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "invalid") with { Limits = Policy() with { QuotaBytes = -1 } },
            CancellationToken.None).ConfigureAwait(false);

        Assert.Equal("bad-traffic-period", template.Code);
        Assert.Equal("bad-traffic-quota", client.Code);
    }

    [Fact]
    public async Task TheMigrationKeepsLegacyLimitsAndMatchesTheCurrentModel()
    {
        using var bench = new Bench();
        var client = await AddClientAsync(bench).ConfigureAwait(false);
        var migrator = bench.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("RoutingDefaults").ConfigureAwait(false);
        await migrator.MigrateAsync("TrafficLimits").ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();

        var saved = await bench.Clients.FindAsync(client.Id, CancellationToken.None).ConfigureAwait(false);
        var templates = await bench.Templates.ListAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(123, saved!.DailyLimit);
        Assert.Null(saved.Limits);
        Assert.False(saved.OverrideLimits);
        Assert.All(templates, template => Assert.Null(template.Limits));
        Assert.Empty(await bench.Db.TrafficPeriods.ToListAsync().ConfigureAwait(false));
        Assert.False(bench.Db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task AClientKeepsEachCalendarWindowAndDeletionRemovesThem()
    {
        using var bench = new Bench();
        var client = await AddClientAsync(bench).ConfigureAwait(false);
        var started = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        foreach (var period in new[] { TrafficLimitPeriod.Day, TrafficLimitPeriod.Week, TrafficLimitPeriod.Month })
        {
            bench.Db.TrafficPeriods.Add(new TrafficPeriodEntity
            {
                ClientId = client.Id,
                Period = period,
                StartedUtc = started,
                EndsUtc = started.AddDays(1),
                Rx = 10,
                Tx = 20,
                SeenRx = 100,
                SeenTx = 200,
                Generation = "peer-1",
                ObservedUtc = started.AddHours(2),
            });
        }

        await bench.Db.SaveChangesAsync().ConfigureAwait(false);
        bench.Db.ChangeTracker.Clear();
        var saved = await bench.Db.TrafficPeriods.AsNoTracking().ToListAsync().ConfigureAwait(false);

        Assert.Equal(3, saved.Count);
        Assert.All(saved, row =>
        {
            Assert.Equal(client.Id, row.ClientId);
            Assert.Equal(started, row.StartedUtc);
            Assert.Equal(started.AddDays(1), row.EndsUtc);
            Assert.Equal(10, row.Rx);
            Assert.Equal(20, row.Tx);
            Assert.Equal(100, row.SeenRx);
            Assert.Equal(200, row.SeenTx);
            Assert.Equal("peer-1", row.Generation);
            Assert.Equal(started.AddHours(2), row.ObservedUtc);
        });
        await bench.Clients.RemoveAsync(client.Id, CancellationToken.None).ConfigureAwait(false);

        Assert.Empty(await bench.Db.TrafficPeriods.ToListAsync().ConfigureAwait(false));
    }

    private static TrafficLimits Policy() => new(
        Enabled: true,
        UploadBitsPerSecond: 20_000_000,
        DownloadBitsPerSecond: 40_000_000,
        QuotaBytes: 1_000_000_000,
        Period: TrafficLimitPeriod.Week,
        ExceededUploadBitsPerSecond: 1_000_000,
        ExceededDownloadBitsPerSecond: 2_000_000);

    private static async Task<TunnelClient> AddClientAsync(
        Bench bench, long? templateId = null, TrafficLimits? limits = null)
    {
        var endpoint = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false);
        var result = await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Record!.Id, "quota") with
            {
                TemplateId = templateId,
                DailyLimit = 123,
                Limits = limits,
                OverrideLimits = limits is not null,
                Address = ["10.8.0.2/32"],
            },
            CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsOk, result.Message);

        return result.Record!;
    }
    [Theory]
    [InlineData("{\"configId\":1,\"name\":\"kept\"}", 7L)]
    [InlineData("{\"configId\":1,\"name\":\"detached\",\"templateId\":null}", null)]
    public void JsonRequestsDistinguishAnOmittedTemplateFromAnExplicitDetach(string json, long? expected)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<ClientRequest>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var held = ClientDefaults.Fresh(1, "existing") with { TemplateId = 7 };
        Assert.Equal(expected, ClientAnswers.Draft(request, held).TemplateId);
    }

}
