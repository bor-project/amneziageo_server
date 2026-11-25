using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class ClientIpv6ApiTests
{
    private const string Six = "fd85:2::1/64";

    [Fact]
    public async Task PreviewAndSaveAddUniqueIpv6WithoutChangingIpv4OrCredentials()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "ipv6852").ConfigureAwait(false);
        var first = (await bench.Clients.AddAsync(Member(endpoint, "first", 42), default).ConfigureAwait(false)).Record!;
        var manual = (await bench.Clients.ImportAsync(Member(endpoint, "manual", 3) with { Address = ["10.85.2.3/32", "fd85:2::2a/128"], IsEnabled = false }, default).ConfigureAwait(false)).Record!;
        var third = (await bench.Clients.AddAsync(Member(endpoint, "third", 12), default).ConfigureAwait(false)).Record!;
        var originals = new[] { first, manual, third }.ToDictionary(one => one.Id);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var request = Request(endpoint with { Address = [.. endpoint.Address, Six] });
        var preview = await PreviewAsync(api, endpoint.Id, request).ConfigureAwait(false);
        Assert.Equal(2, preview.Clients.Count);
        Assert.All(preview.Clients, one => Assert.Contains("[interface].address", one.Fields));
        Assert.DoesNotContain(preview.Clients, one => one.Id == manual.Id);
        Assert.All(await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false), one => Assert.Equal(originals[one.Id].Address, one.Address));
        bench.Clock.Pass(TimeSpan.FromMinutes(1));
        using var applied = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", request with { ExpectedRevision = preview.BaseRevision, ExpectedClientsRevision = preview.ClientsRevision }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var clients = await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false);
        var actual = (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!;
        Assert.Equal("fd85:2::2/128", Assert.Single(SixOf(clients.Single(one => one.Id == first.Id))));
        Assert.Equal("fd85:2::c/128", Assert.Single(SixOf(clients.Single(one => one.Id == third.Id))));
        Assert.Equal(manual.Address, clients.Single(one => one.Id == manual.Id).Address);
        Assert.Equal(3, clients.SelectMany(SixOf).Select(AwgAllowedIp.Parse).Select(one => one.Address).Distinct().Count());
        foreach (var client in clients)
        {
            Assert.Equal(Metadata(originals[client.Id]), Metadata(client));
            Assert.Equal(FourOf(originals[client.Id]), FourOf(client));
            var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
            Assert.Contains(string.Join(", ", client.Address), exported.Text, StringComparison.Ordinal);
            var peer = ClientDevice.Peer(actual, client);
            Assert.Equal(client.Address.Select(AwgAllowedIp.Parse).ToHashSet(), peer.AllowedIps.ToHashSet());
            if (preview.Clients.SingleOrDefault(one => one.Id == client.Id) is { } changed)
            {
                Assert.Equal(changed.Revision, ConfigurationRevision.Of(exported.Text));
            }
        }
        var unchanged = await PreviewAsync(api, endpoint.Id, request).ConfigureAwait(false);
        Assert.Empty(unchanged.Clients);
        bench.Clock.Pass(TimeSpan.FromHours(1));
        Assert.True((await bench.Configs.SynchronizeClientsAsync(endpoint.Id, default).ConfigureAwait(false)).IsOk);
        foreach (var client in await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false))
        {
            var previous = clients.Single(one => one.Id == client.Id);
            Assert.Equal(previous.Address, client.Address);
            Assert.Equal(previous.UpdatedUtc, client.UpdatedUtc);
        }
    }

    [Fact]
    public async Task DeferringIpv6KeepsActiveProfilesUntilPendingApplication()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "later852").ConfigureAwait(false);
        var client = (await bench.Clients.AddAsync(Member(endpoint, "phone", 8), default).ConfigureAwait(false)).Record!;
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var request = Request(endpoint with { Address = [.. endpoint.Address, Six] });
        var preview = await PreviewAsync(api, endpoint.Id, request).ConfigureAwait(false);
        Assert.Single(preview.Clients);
        using var saved = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", request with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(endpoint.Address, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Address);
        Assert.Equal(client.Address, (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!.Address);
        var pending = (await api.GetFromJsonAsync<PendingConfigBody>($"/api/configs/{endpoint.Id}/pending").ConfigureAwait(false))!;
        Assert.False(pending.Stale);
        Assert.Equal(preview.Clients[0].Revision, pending.Preview.Clients[0].Revision);
        using var applied = await api.PostAsync($"/api/configs/{endpoint.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var member = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        Assert.Equal(["10.85.2.8/32", "fd85:2::8/128"], member.Address);
        Assert.Equal(Metadata(client), Metadata(member));
        var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
        Assert.Equal(preview.Clients[0].Revision, ConfigurationRevision.Of(exported.Text));
    }

    [Fact]
    public async Task ANewClientMakesThePendingProposalStaleAndCancelKeepsIpv4()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "stale852").ConfigureAwait(false);
        var client = (await bench.Clients.AddAsync(Member(endpoint, "phone", 2), default).ConfigureAwait(false)).Record!;
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var saved = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", Request(endpoint with { Address = [.. endpoint.Address, Six] }) with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.True((await bench.Clients.AddAsync(Member(endpoint, "newphone", 3), default).ConfigureAwait(false)).IsOk);
        Assert.True((await api.GetFromJsonAsync<PendingConfigBody>($"/api/configs/{endpoint.Id}/pending").ConfigureAwait(false))!.Stale);
        using var stale = await api.PostAsync($"/api/configs/{endpoint.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("config-change-stale", await stale.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
        Assert.All(await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false), one => Assert.Empty(SixOf(one)));
        using var cancelled = await api.DeleteAsync($"/api/configs/{endpoint.Id}/pending").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        Assert.Equal(client.Address, (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!.Address);
    }

    [Theory]
    [InlineData(128, 1)]
    [InlineData(126, 3)]
    public async Task ExhaustionRefusesTheWholeChangeWithoutPartiallyAllocating(int prefix, int count)
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "full852").ConfigureAwait(false);
        for (var number = 2; number < count + 2; number++)
        {
            Assert.True((await bench.Clients.AddAsync(Member(endpoint, "phone" + number, number), default).ConfigureAwait(false)).IsOk);
        }
        var before = await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var request = Request(endpoint with { Address = [.. endpoint.Address, $"fd85:2::1/{prefix}"] });
        using var preview = await api.PostAsJsonAsync($"/api/configs/{endpoint.Id}/preview", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Contains("client-ipv6-unavailable", await preview.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
        using var saved = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, saved.StatusCode);
        Assert.Equal(endpoint.Address, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Address);
        foreach (var member in await bench.Clients.ListAsync(endpoint.Id, default).ConfigureAwait(false))
        {
            Assert.Equal(before.Single(one => one.Id == member.Id).Address, member.Address);
            Assert.Empty(SixOf(member));
        }
    }

    [Fact]
    public async Task ExplicitApplyRepairsExistingDualStackInterfacesIdempotently()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "repair852", ["10.85.2.1/24", Six]).ConfigureAwait(false);
        var original = (await bench.Clients.ImportAsync(Member(endpoint, "legacy", 17), default).ConfigureAwait(false)).Record!;
        Assert.Empty(SixOf(original));
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var applied = await api.PostAsync($"/api/configs/{endpoint.Id}/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var client = (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!;
        Assert.Equal(["10.85.2.17/32", "fd85:2::11/128"], client.Address);
        Assert.Equal(Metadata(original), Metadata(client));
        using var repeated = await api.PostAsync($"/api/configs/{endpoint.Id}/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(client.Address, (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!.Address);
    }

    [Fact]
    public async Task ApplyingAllInterfacesDoesNotPartiallyWriteWhenOnePoolIsFull()
    {
        using var bench = new Bench();
        var first = await EndpointAsync(bench, "all852a", ["10.85.2.1/24", Six]).ConfigureAwait(false);
        var second = await EndpointAsync(bench, "all852b", ["10.85.3.1/24", "fd85:3::1/128"]).ConfigureAwait(false);
        var one = (await bench.Clients.ImportAsync(Member(first, "one", 2), default).ConfigureAwait(false)).Record!;
        var two = (await bench.Clients.ImportAsync(Member(second, "two", 2) with { Address = ["10.85.3.2/32"] }, default).ConfigureAwait(false)).Record!;
        var result = await bench.Configs.SynchronizeClientsAsync(null, default).ConfigureAwait(false);
        Assert.False(result.IsOk);
        Assert.Equal("client-ipv6-unavailable", result.Code);
        Assert.Equal(one.Address, (await bench.Clients.FindAsync(one.Id, default).ConfigureAwait(false))!.Address);
        Assert.Equal(two.Address, (await bench.Clients.FindAsync(two.Id, default).ConfigureAwait(false))!.Address);
    }

    [Fact]
    public async Task AConcurrentClientWriteWaitsForTheInterfaceTransactionWithoutDeadlocking()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "race852").ConfigureAwait(false);
        var existing = (await bench.Clients.AddAsync(Member(endpoint, "oldphone", 2), default).ConfigureAwait(false)).Record!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={bench.DatabasePath}").Options;
        await using var other = new AppDbContext(options);
        var clients = new ClientStore(other, bench.Clock);
        await using var transaction = await bench.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable).ConfigureAwait(false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adding = Task.Run(async () =>
        {
            started.SetResult();
            return await clients.AddAsync(Member(endpoint, "newphone", 9), default).ConfigureAwait(false);
        });
        await started.Task.ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        var changed = await bench.Configs.ChangeAsync(endpoint.Id, endpoint with { Address = [.. endpoint.Address, Six] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk);
        await transaction.CommitAsync().ConfigureAwait(false);
        var created = await adding.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.True(created.IsOk);
        Assert.Equal(["10.85.2.9/32", "fd85:2::9/128"], created.Record!.Address);
        var old = (await bench.Clients.FindAsync(existing.Id, default).ConfigureAwait(false))!;
        Assert.Equal(["10.85.2.2/32", "fd85:2::2/128"], old.Address);
    }

    [Fact]
    public async Task ClientStoresSharingAContextSerializeTheirTransactions()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "queue852", ["10.85.2.1/24", Six]).ConfigureAwait(false);
        var pause = new PausedSave();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={bench.DatabasePath}").AddInterceptors(pause).Options;
        await using var db = new AppDbContext(options);
        var first = new ClientStore(db, bench.Clock);
        var second = new ClientStore(db, bench.Clock);
        var addingFirst = first.AddAsync(Member(endpoint, "first", 2) with { Address = [] }, default);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var addingSecond = second.AddAsync(Member(endpoint, "second", 3) with { Address = [] }, default);
        try
        {
            await Task.Delay(100).ConfigureAwait(false);
            Assert.False(addingSecond.IsCompleted);
        }
        finally
        {
            pause.Release.TrySetResult();
        }
        var results = await Task.WhenAll(addingFirst, addingSecond).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.All(results, one => Assert.True(one.IsOk, one.Code));
        Assert.Equal(4, results.SelectMany(one => one.Record!.Address).Distinct().Count());
    }

    private sealed class PausedSave : SaveChangesInterceptor
    {
        private int _pending = 1;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _pending, 0) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return result;
        }
    }

    [Fact]
    public async Task SavingAnOldClientDraftPreservesIpv6AndIndividualAddresses()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "draft852").ConfigureAwait(false);
        var original = (await bench.Clients.AddAsync(Member(endpoint, "phone", 15), default).ConfigureAwait(false)).Record!;
        Assert.True((await bench.Configs.ChangeAsync(endpoint.Id, endpoint with { Address = [.. endpoint.Address, Six] }, default).ConfigureAwait(false)).IsOk);
        var changed = await bench.Clients.ChangeAsync(original.Id, original with { Note = "changed" }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk);
        Assert.Equal(["10.85.2.15/32", "fd85:2::f/128"], changed.Record!.Address);
        var manual = await bench.Clients.ChangeAsync(original.Id, changed.Record with { Address = ["10.85.2.15/32", "fd85:2::abcd/128"] }, default).ConfigureAwait(false);
        Assert.True(manual.IsOk);
        Assert.True((await bench.Configs.SynchronizeClientsAsync(endpoint.Id, default).ConfigureAwait(false)).IsOk);
        Assert.Equal(manual.Record!.Address, (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!.Address);
    }

    [Fact]
    public async Task TheSameSubscriptionUrlOffersUpdatedIpv6AndHelloRevisions()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "hello852").ConfigureAwait(false);
        Assert.True((await bench.Configs.SwitchAsync(endpoint.Id, true, default).ConfigureAwait(false)).IsOk);
        endpoint = (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!;
        var client = (await bench.Clients.AddAsync(Member(endpoint, "phone", 22) with { SubscriptionId = "same-subscription852" }, default).ConfigureAwait(false)).Record!;
        var state = new SubscriptionState { Current = SubscriptionDefaults.Settings with { IsEnabled = true } };
        var offer = new SubscriptionOffer(state, PanelDefaults.Settings, new WebOptions(), bench.Scopes);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("vpn.example");
        var before = Assert.IsType<SubscriptionFeature>(await offer.OfferAsync(new HelloPeer(client, endpoint, null, context), default).ConfigureAwait(false));
        var changed = await bench.Configs.ChangeAsync(endpoint.Id, endpoint with { Address = [.. endpoint.Address, Six] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk);
        var current = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        var after = Assert.IsType<SubscriptionFeature>(await offer.OfferAsync(new HelloPeer(current, changed.Record!, null, context), default).ConfigureAwait(false));
        Assert.Equal(before.Url, after.Url);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.NotEqual(before.ConfigurationRevision, after.ConfigurationRevision);
        Assert.Equal(Metadata(client), Metadata(current));
        var feed = await bench.Services.GetRequiredService<SubscriptionFeed>().ReadAsync(current.SubscriptionId, "vpn.example", default).ConfigureAwait(false);
        var sheet = Assert.Single(feed!.Sheets);
        Assert.Equal(current.Id, sheet.MemberId);
        Assert.Contains("10.85.2.22/32, fd85:2::16/128", sheet.Text, StringComparison.Ordinal);
        Assert.Equal(ConfigurationRevision.Of(sheet.Text), after.ConfigurationRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterfaceIdentityComesFromTheRequestedId(bool foreignId)
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "identity852a").ConfigureAwait(false);
        var other = await EndpointAsync(bench, "identity852b", ["10.85.3.1/24"]).ConfigureAwait(false);
        var client = (await bench.Clients.AddAsync(Member(endpoint, "first", 2), default).ConfigureAwait(false)).Record!;
        var unrelated = (await bench.Clients.AddAsync(Member(other, "second", 2) with { Address = ["10.85.3.2/32"] }, default).ConfigureAwait(false)).Record!;
        var draft = endpoint with { Id = foreignId ? other.Id : 0, Address = [.. endpoint.Address, Six] };
        var result = await bench.Configs.ChangeAsync(endpoint.Id, draft, default).ConfigureAwait(false);
        Assert.True(result.IsOk);
        Assert.Equal(endpoint.Id, result.Record!.Id);
        var current = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        Assert.Equal(["10.85.2.2/32", "fd85:2::2/128"], current.Address);
        Assert.Equal(Metadata(client), Metadata(current));
        var untouched = (await bench.Clients.FindAsync(unrelated.Id, default).ConfigureAwait(false))!;
        Assert.Equal(unrelated.Address, untouched.Address);
        Assert.Equal(Metadata(unrelated), Metadata(untouched));
    }

    [Fact]
    public async Task ReusedContextsPlanFromTheLatestPersistedAddresses()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "fresh852").ConfigureAwait(false);
        var client = (await bench.Clients.AddAsync(Member(endpoint, "phone", 2), default).ConfigureAwait(false)).Record!;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={bench.DatabasePath}").Options;
        await using var other = new AppDbContext(options);
        var configs = new ConfigStore(other, bench.Clock);
        Assert.True((await configs.ChangeAsync(endpoint.Id, endpoint with { Address = [.. endpoint.Address, Six] }, default).ConfigureAwait(false)).IsOk);
        var clients = new ClientStore(other, bench.Clock);
        var member = (await clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        var custom = await clients.ChangeAsync(member.Id, member with { Address = ["10.85.2.2/32", "fd85:2::abcd/128"], Note = "external update" }, default).ConfigureAwait(false);
        Assert.True(custom.IsOk);
        var applied = await bench.Configs.SynchronizeClientsAsync(endpoint.Id, default).ConfigureAwait(false);
        Assert.True(applied.IsOk);
        Assert.Contains(Six, applied.Record!.Address);
        var current = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        Assert.Equal(custom.Record!.Address, current.Address);
        Assert.Equal(Metadata(custom.Record), Metadata(current));
    }

    [Fact]
    public async Task UserDefinedIpv4RoutesAreNotExpandedByAddressSynchronization()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "routes852").ConfigureAwait(false);
        var client = (await bench.Clients.AddAsync(Member(endpoint, "phone", 2), default).ConfigureAwait(false)).Record!;
        var changed = await bench.Configs.ChangeAsync(endpoint.Id, endpoint with { Address = [.. endpoint.Address, Six], AllowedIps = ["0.0.0.0/0"] }, default).ConfigureAwait(false);
        Assert.True(changed.IsOk);
        var member = (await bench.Clients.FindAsync(client.Id, default).ConfigureAwait(false))!;
        var text = ClientText.Text(changed.Record!, member);
        Assert.Contains("fd85:2::2/128", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 0.0.0.0/0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("::/0", text, StringComparison.Ordinal);
    }

    private static async Task<ServerConfig> EndpointAsync(Bench bench, string name, IReadOnlyList<string>? addresses = null) =>
        (await bench.Configs.AddAsync(ConfigDefaults.Fresh(name) with { IsEnabled = false, Host = "vpn.example", Address = addresses ?? ["10.85.2.1/24"] }, default).ConfigureAwait(false)).Record!;

    private static TunnelClient Member(ServerConfig endpoint, string name, int number) => ClientDefaults.Fresh(endpoint.Id, name) with { Address = [$"10.85.2.{number}/32"] };

    private static IEnumerable<string> SixOf(TunnelClient client) => client.Address.Where(one => AwgAllowedIp.Parse(one).IsSix);
    private static IEnumerable<string> FourOf(TunnelClient client) => client.Address.Where(one => !AwgAllowedIp.Parse(one).IsSix);
    private static string Metadata(TunnelClient client) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(client with { Address = [], UpdatedUtc = default })));

    private static async Task<ConfigChangePreview> PreviewAsync(HttpClient api, long id, ConfigRequest request)
    {
        using var response = await api.PostAsJsonAsync($"/api/configs/{id}/preview", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConfigChangePreview>().ConfigureAwait(false))!;
    }

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, config.IsEnabled,
        config.Nat, false, [.. config.Blocked], config.PrivateKey, config.PresharedKey,
        ConfigAnswers.Obfuscation(config.Obfuscation, true), WebSocket: false, ServicesPort: config.ListenPort);
}
