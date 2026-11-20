using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Traffic;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class TrafficPeriodSubscriptionTests
{
    [Theory]
    [InlineData("block", 1000)]
    [InlineData("throttle", 0)]
    public async Task ASubscriptionUsesTheQuotaPeriodAndKeepsSoftQuotasConnectable(string action, int total)
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg1") with { Address = ["10.8.0.1/24"] },
            CancellationToken.None).ConfigureAwait(false)).Record!;
        var client = (await bench.Clients.AddAsync(
            ClientDefaults.Fresh(endpoint.Id, "limited") with
            {
                Address = ["10.8.0.2/32"], DailyLimit = 100, OverrideLimits = true,
                Limits = new TrafficLimits(Enabled: true, QuotaBytes: 1000, Period: "month", QuotaAction: action,
                    ExceededUploadBitsPerSecond: 128, ExceededDownloadBitsPerSecond: 256),
            }, CancellationToken.None).ConfigureAwait(false)).Record!;
        var ledger = bench.Services.GetRequiredService<TrafficLedger>();
        ledger.SetLimits([client], []);
        ledger.Observe([client], [new(client.Id, 0, 0)], bench.Clock.Now);
        ledger.Observe([client], [new(client.Id, 30, 40)], bench.Clock.Now.AddSeconds(2));
        var feed = await bench.Services.GetRequiredService<SubscriptionFeed>()
            .ReadAsync(client.SubscriptionId, "vpn.example.org", CancellationToken.None).ConfigureAwait(false);
        Assert.NotNull(feed);
        Assert.Equal(30UL, feed.Upload);
        Assert.Equal(40UL, feed.Download);
        Assert.Equal((ulong)total, feed.Total);
        Assert.Contains($"total={total}; expire=0", feed.Usage, StringComparison.Ordinal);
    }
}
