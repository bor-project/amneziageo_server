using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.EntityFrameworkCore;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class InterfaceChangeTests
{
    [Fact]
    public void AllExportedAwgFieldsChangeTheRevisionWithoutDisclosingValues()
    {
        var endpoint = ConfigDefaults.Fresh("revision");
        var client = ClientDefaults.Fresh(1, "phone");
        var current = ClientText.Text(endpoint, client);
        foreach (var property in typeof(ObfuscationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var cover = endpoint.Obfuscation with { };
            var value = property.GetValue(cover);
            property.SetValue(cover, property.PropertyType == typeof(int) ? (int)value! + 1
                : property.PropertyType == typeof(bool) ? !(bool)value! : (value?.ToString() ?? string.Empty) + "changed");
            var changed = ClientText.Text(endpoint with { Obfuscation = cover }, client);
            Assert.NotEqual(ConfigurationRevision.Of(current), ConfigurationRevision.Of(changed));
            Assert.Contains(ConfigurationRevision.Fields(current, changed), one => one.EndsWith("." + property.Name, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(ConfigurationRevision.Fields(current, changed), one => one.Contains("changed", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task OverridesAndServerOnlySettingsDoNotProduceFalseUpdates()
    {
        using var bench = new Bench();
        var endpoint = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("revision"), default).ConfigureAwait(false)).Record!;
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "custom", Mtu = 1280, Keepalive = 10 }, default).ConfigureAwait(false)).Record!;
        await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "inherited"), default).ConfigureAwait(false);
        await bench.Clients.AddAsync(ClientDefaults.Fresh(endpoint.Id, "overridden") with { TemplateId = template.Id }, default).ConfigureAwait(false);
        var reader = new ConfigChangeReader(bench.Clients, bench.Templates, bench.Resolver, new DnsState());
        var impact = await reader.PreviewAsync(endpoint, endpoint with { Mtu = 1300, Keepalive = 7 }, default).ConfigureAwait(false);
        Assert.Equal("inherited", Assert.Single(impact.Clients).Name);
        Assert.Empty((await reader.PreviewAsync(endpoint, endpoint with { Nat = !endpoint.Nat, OfflineAfter = endpoint.OfflineAfter + 1 }, default).ConfigureAwait(false)).Clients);
        Assert.Empty((await reader.PreviewAsync(endpoint, endpoint, default).ConfigureAwait(false)).Clients);
    }

    [Fact]
    public void RevisionsIncludeFutureFieldsAndPreserveFormattingCompatibility()
    {
        const string first = "[Interface]\nHeaderProtectionKey = secret\nFutureField = first\n[Peer]\nEndpoint = host:1234\n";
        Assert.Equal(ConfigurationRevision.Of(first), ConfigurationRevision.Of(first.Replace(" = ", "=").Replace("Endpoint", "endpoint").Replace("\n", "\r\n")));
        var next = first.Replace("first", "next");
        Assert.NotEqual(ConfigurationRevision.Of(first), ConfigurationRevision.Of(next));
        Assert.Equal(["[interface].futurefield"], ConfigurationRevision.Fields(first, next));
    }

    [Fact]
    public async Task DeferredChangesSurviveRestartAndLeaveActiveExportsUntouched()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("pending838") with { IsEnabled = false, Host = "vpn.example", Address = ["10.83.8.1/24"] };
        using var created = await api.PostAsJsonAsync("/api/configs", Request(endpoint)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var held = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(held.Id, "phone"), default).ConfigureAwait(false)).Record!;
        var before = ClientText.Text((await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!, client);
        var draft = Request(endpoint with { Mtu = 1300 });
        using var preview = await api.PostAsJsonAsync($"/api/configs/{held.Id}/preview", draft).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var impact = (await preview.Content.ReadFromJsonAsync<ConfigChangePreview>().ConfigureAwait(false))!;
        Assert.Single(impact.Clients);
        using var saved = await api.PutAsJsonAsync($"/api/configs/{held.Id}", draft with { Defer = true, ExpectedRevision = impact.BaseRevision }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(held.Mtu, (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{held.Id}").ConfigureAwait(false))!.Mtu);
        Assert.Equal(before, ClientText.Text((await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!, client));
        bench.Db.ChangeTracker.Clear();
        Assert.NotNull(await new ConfigDraftStore(bench.Db, bench.Clock).FindAsync(held.Id, default).ConfigureAwait(false));
        var pending = await api.GetFromJsonAsync<PendingConfigBody>($"/api/configs/{held.Id}/pending").ConfigureAwait(false);
        Assert.False(pending!.Stale);
        using var applied = await api.PostAsync($"/api/configs/{held.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(1300, (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{held.Id}").ConfigureAwait(false))!.Mtu);
        using var empty = await api.GetAsync($"/api/configs/{held.Id}/pending").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
    }

    [Fact]
    public async Task CancelAndConcurrentChangesDoNotApplyTheDraft()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("stale838") with { IsEnabled = false, Host = "vpn.example" };
        using var created = await api.PostAsJsonAsync("/api/configs", Request(endpoint)).ConfigureAwait(false);
        var held = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        using var saved = await api.PutAsJsonAsync($"/api/configs/{held.Id}", Request(endpoint with { Mtu = 1300 }) with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await bench.Configs.SwitchAsync(held.Id, false, default).ConfigureAwait(false);
        using var stale = await api.PostAsync($"/api/configs/{held.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(held.Mtu, (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{held.Id}").ConfigureAwait(false))!.Mtu);
        using var cancelled = await api.DeleteAsync($"/api/configs/{held.Id}/pending").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        using var gone = await api.PostAsync($"/api/configs/{held.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task AChangedEffectiveTemplateInvalidatesThePendingProposal()
    {
        using var bench = new Bench();
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("template838") with { IsEnabled = false, Host = "vpn.example" };
        using var created = await api.PostAsJsonAsync("/api/configs", Request(endpoint)).ConfigureAwait(false);
        var held = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        await bench.Clients.AddAsync(ClientDefaults.Fresh(held.Id, "phone"), default).ConfigureAwait(false);
        using var saved = await api.PutAsJsonAsync($"/api/configs/{held.Id}", Request(endpoint with { Mtu = 1300 }) with { Defer = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var template = await bench.Templates.DefaultAsync(default).ConfigureAwait(false);
        Assert.True((await bench.Templates.ChangeDefaultAsync(template with { Mtu = 1280 }, null, default).ConfigureAwait(false)).IsOk);
        Assert.True((await api.GetFromJsonAsync<PendingConfigBody>($"/api/configs/{held.Id}/pending").ConfigureAwait(false))!.Stale);
        using var applied = await api.PostAsync($"/api/configs/{held.Id}/pending/apply", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, applied.StatusCode);
        Assert.Equal(held.Mtu, (await api.GetFromJsonAsync<ConfigResponse>($"/api/configs/{held.Id}").ConfigureAwait(false))!.Mtu);
    }

    [Fact]
    public void UnknownAndWireFormatFieldsRequireCoordination()
    {
        Assert.True(ConfigurationRevision.RequiresCoordination(["[interface].H1", "[interface].HeaderProtectionKey"]));
        Assert.True(ConfigurationRevision.RequiresCoordination(["[interface].FutureField"]));
        Assert.False(ConfigurationRevision.RequiresCoordination(["[interface].Jc", "[interface].I1", "[interface].RekeyTimeout"]));
    }

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, config.IsEnabled, config.Nat,
        config.Opened, [.. config.Blocked], config.PrivateKey, config.PresharedKey, ConfigAnswers.Obfuscation(config.Obfuscation, true));
}
