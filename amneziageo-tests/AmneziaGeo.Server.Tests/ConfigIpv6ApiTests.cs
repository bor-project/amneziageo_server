using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class ConfigIpv6ApiTests
{
    private const string Four = "10.85.1.1/24";
    private const string Six = "fd85:1::1/64";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewDraftReflectsTheCurrentIpv6PathWithoutWritingTheDatabase(bool available)
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(available))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var draft = (await api.GetFromJsonAsync<ConfigResponse>("/api/configs/draft?name=new851").ConfigureAwait(false))!;
        Assert.Equal(ConfigDefaults.Subnet, draft.Address[0]);
        Assert.Equal(available ? 2 : 1, draft.Address.Length);
        Assert.Equal(available, draft.Ipv6AutoAddress.Length > 0);
        Assert.Empty(await bench.Configs.ListAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task RefreshChangesOnlyTheDraftAndSavePersistsTheMarker()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "draft851").ConfigureAwait(false);
        var original = (await bench.Clients.AddAsync(Member(endpoint), default).ConfigureAwait(false)).Record!;
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(true))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var refresh = await RefreshAsync(api, new(endpoint.Id, [.. endpoint.Address], "")).ConfigureAwait(false);
        Assert.True(refresh.Available);
        Assert.NotEmpty(refresh.Ipv6AutoAddress);
        Assert.Equal(Four, refresh.Address[0]);
        Assert.Equal(endpoint.Address, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Address);
        Assert.Equal(original.Address, (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!.Address);
        var request = Request(endpoint with { Address = refresh.Address, Ipv6AutoAddress = refresh.Ipv6AutoAddress });
        using var saved = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var reopened = (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{endpoint.Id}").ConfigureAwait(false))!;
        Assert.Equal(refresh.Address, reopened.Address);
        Assert.Equal(refresh.Ipv6AutoAddress, reopened.Ipv6AutoAddress);
        Assert.Equal(endpoint.PrivateKey, reopened.PrivateKey);
        Assert.Equal(endpoint.PublicKey, reopened.PublicKey);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={bench.DatabasePath}").Options;
        await using var database = new AppDbContext(options);
        Assert.Equal(refresh.Ipv6AutoAddress, (await new ConfigStore(database).FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Ipv6AutoAddress);
        var current = (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!;
        Assert.Equal(Metadata(original), Metadata(current));
        Assert.Contains("10.85.1.7/32", current.Address);
        Assert.Equal(2, current.Address.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fd85:9::1/64")]
    public async Task ExistingManualIpv6AndStaleMarkersDoNotChangeAddresses(string marker)
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "manual851", [Four, Six]).ConfigureAwait(false);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(false))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var refreshed = await RefreshAsync(api, new(endpoint.Id, [.. endpoint.Address], marker)).ConfigureAwait(false);
        Assert.Equal(endpoint.Address, refreshed.Address);
        Assert.Empty(refreshed.Ipv6AutoAddress);
        Assert.Equal(endpoint.Address, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Address);
    }

    [Theory]
    [InlineData("probe-failed")]
    [InlineData("unsupported")]
    public async Task TechnicalProbeFailuresDoNotReturnAChangedDraft(string reason)
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "failed851", [Four, Six], Six).ConfigureAwait(false);
        var original = (await bench.Clients.AddAsync(Member(endpoint), default).ConfigureAwait(false)).Record!;
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(false, reason))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await api.PostAsJsonAsync("/api/configs/ipv6", new ConfigIpv6Request(endpoint.Id, [.. endpoint.Address], endpoint.Ipv6AutoAddress)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var failure = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Equal("ipv6-check-failed", failure.RootElement.GetProperty("error").GetString());
        Assert.False(failure.RootElement.TryGetProperty("address", out _));
        Assert.False(failure.RootElement.TryGetProperty("ipv6AutoAddress", out _));
        var unchanged = (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!;
        Assert.Equal(endpoint.Address, unchanged.Address);
        Assert.Equal(endpoint.Ipv6AutoAddress, unchanged.Ipv6AutoAddress);
        Assert.Equal(endpoint.PrivateKey, unchanged.PrivateKey);
        var client = (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!;
        Assert.Equal(original.Address, client.Address);
        Assert.Equal(Metadata(original), Metadata(client));
    }

    [Fact]
    public async Task MissingInterfacesAreRejectedBeforeProbing()
    {
        using var bench = new Bench();
        var probe = new Probe(true);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(probe)).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await api.PostAsJsonAsync("/api/configs/ipv6", new ConfigIpv6Request(999999, [Four], "")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("fd85::1/129")]
    public async Task InvalidAddressesAreRejectedBeforeProbing(string address)
    {
        using var bench = new Bench();
        var probe = new Probe(true);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(probe)).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await api.PostAsJsonAsync("/api/configs/ipv6", new ConfigIpv6Request(null, [address], "")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, probe.Calls);
        Assert.Empty(await bench.Configs.ListAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task AnAutomaticSubnetCannotOverlapADisabledInterfaceOnSave()
    {
        using var bench = new Bench();
        var occupied = await EndpointAsync(bench, "held851", [Four, "fd85::1/16"]).ConfigureAwait(false);
        Assert.False(occupied.IsEnabled);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(true))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var draft = ConfigDefaults.Fresh("taken851") with { IsEnabled = false, Host = "vpn.example", Address = ["10.85.9.1/24", Six], Ipv6AutoAddress = Six };
        using var response = await api.PostAsJsonAsync("/api/configs", Request(draft)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ipv6-auto-address-taken", await response.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
        Assert.Single(await bench.Configs.ListAsync(default).ConfigureAwait(false));
    }

    [Fact]
    public async Task LosingIpv6IsDeferredUntilApplyAndPreservesIpv4AndKeys()
    {
        using var bench = new Bench();
        var endpoint = await EndpointAsync(bench, "retire851", [Four, Six], Six).ConfigureAwait(false);
        var original = (await bench.Clients.AddAsync(Member(endpoint), default).ConfigureAwait(false)).Record!;
        Assert.Contains("fd85:1::7/128", original.Address);
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, services: services => services.AddSingleton<IIpv6Probe>(new Probe(false))).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var refresh = await RefreshAsync(api, new(endpoint.Id, [.. endpoint.Address], endpoint.Ipv6AutoAddress)).ConfigureAwait(false);
        Assert.Equal([Four], refresh.Address);
        Assert.Empty(refresh.Ipv6AutoAddress);
        var request = Request(endpoint with { Address = refresh.Address, Ipv6AutoAddress = refresh.Ipv6AutoAddress });
        using var previewResponse = await api.PostAsJsonAsync($"/api/configs/{endpoint.Id}/preview", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ConfigChangePreview>().ConfigureAwait(false))!;
        Assert.Single(preview.Clients);
        Assert.Contains("[interface].address", preview.Clients[0].Fields);
        using var later = await api.PutAsJsonAsync($"/api/configs/{endpoint.Id}", request with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Equal(endpoint.Address, (await bench.Configs.FindAsync(endpoint.Id, default).ConfigureAwait(false))!.Address);
        Assert.Equal(original.Address, (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!.Address);
        var pending = (await api.GetFromJsonAsync<PendingConfigBody>($"/api/configs/{endpoint.Id}/pending").ConfigureAwait(false))!;
        Assert.False(pending.Stale);
        Assert.Equal(preview.Clients[0].Revision, pending.Preview.Clients[0].Revision);
        using var apply = await api.PostAsync($"/api/configs/{endpoint.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var current = (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!;
        Assert.Equal(["10.85.1.7/32"], current.Address);
        Assert.Equal(Metadata(original), Metadata(current));
        var after = (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{endpoint.Id}").ConfigureAwait(false))!;
        Assert.Empty(after.Ipv6AutoAddress);
        Assert.Equal(endpoint.PrivateKey, after.PrivateKey);
        var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{current.Id}/config").ConfigureAwait(false))!;
        Assert.Equal(preview.Clients[0].Revision, ConfigurationRevision.Of(exported.Text));
        Assert.DoesNotContain("fd85:1::", exported.Text, StringComparison.Ordinal);
    }

    private static async Task<ConfigIpv6Response> RefreshAsync(HttpClient api, ConfigIpv6Request request)
    {
        using var response = await api.PostAsJsonAsync("/api/configs/ipv6", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConfigIpv6Response>().ConfigureAwait(false))!;
    }

    private static async Task<ServerConfig> EndpointAsync(Bench bench, string name, IReadOnlyList<string>? addresses = null, string automatic = "")
    {
        var result = await bench.Configs.AddAsync(ConfigDefaults.Fresh(name) with { IsEnabled = false, Host = "vpn.example", Address = addresses ?? [Four], Ipv6AutoAddress = automatic }, default).ConfigureAwait(false);
        Assert.True(result.IsOk, result.Message);
        return result.Record!;
    }

    private static TunnelClient Member(ServerConfig endpoint) => ClientDefaults.Fresh(endpoint.Id, "phone") with { Address = ["10.85.1.7/32"] };

    private static string Metadata(TunnelClient client) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(client with { Address = [], UpdatedUtc = default })));

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, config.IsEnabled,
        config.Nat, false, [.. config.Blocked], config.PrivateKey, config.PresharedKey,
        ConfigAnswers.Obfuscation(config.Obfuscation, true), config.OfflineAfter, WebSocket: false, ServicesPort: config.ListenPort,
        Ipv6AutoAddress: config.Ipv6AutoAddress);

    private sealed class Probe(bool available, string? reason = null) : IIpv6Probe
    {
        public int Calls { get; private set; }

        public Task<Ipv6Reach> CheckAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new Ipv6Reach(available, reason ?? (available ? "available" : "unreachable")));
        }
    }
}
