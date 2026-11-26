using System.Net;
using System.Net.Http.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Api.Names;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class PlainWireGuardApiTests
{
    [Fact]
    public async Task PlainEndpointsCanBeCreatedUpdatedAndExportedThroughTheRealApi()
    {
        using var bench = new Bench();
        await using var app = await CreateAsync(bench).ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var endpoint = ConfigDefaults.Fresh("plain771") with { IsEnabled = false, Host = "vpn.example", Address = ["10.77.171.1/24"] };
        var request = Request(endpoint with { Obfuscation = ConfigDefaults.Plain() });
        using var created = await api.PostAsJsonAsync("/api/configs", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var config = (await created.Content.ReadFromJsonAsync<ConfigResponse>().ConfigureAwait(false))!;
        Assert.True(ConfigRules.IsPlain(ConfigAnswers.Settings(config.Obfuscation)));

        var pair = Curve25519.Create();
        var clientRequest = new ClientRequest(config.Id, "plain-client", pair.PrivateKey, pair.PublicKey, "", ["10.77.171.2/32"], false, "");
        using var made = await api.PostAsJsonAsync("/api/clients", clientRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var client = (await made.Content.ReadFromJsonAsync<ClientResponse>().ConfigureAwait(false))!;
        var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
        Assert.StartsWith("wireguard://", exported.Wireguard);
        Assert.StartsWith("vpn://", exported.Link);
        Assert.Contains("[Interface]", exported.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("H1 =", exported.Text, StringComparison.Ordinal);

        using var mixed = await api.PutAsJsonAsync($"/api/configs/{config.Id}", request with
        {
            Obfuscation = ConfigAnswers.Obfuscation(ConfigDefaults.Plain() with { H1 = "5" }, true),
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        Assert.Contains("bad-type", await mixed.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);

        using var obfuscated = await api.PutAsJsonAsync($"/api/configs/{config.Id}", Request(endpoint)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, obfuscated.StatusCode);
        Assert.Empty((await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!.Wireguard);
        using var plain = await api.PutAsJsonAsync($"/api/configs/{config.Id}", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.NotEmpty((await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!.Wireguard);

        using var preshared = await api.PutAsJsonAsync($"/api/clients/{client.Id}", clientRequest with { PresharedKey = Curve25519.Create().PrivateKey }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, preshared.StatusCode);
        var withKey = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{client.Id}/config").ConfigureAwait(false))!;
        Assert.Empty(withKey.Wireguard);
        Assert.Contains("PresharedKey =", withKey.Text, StringComparison.Ordinal);
    }

    public static async Task<WebApplication> CreateAsync(Bench bench, int port = 0, Action<WebApplication>? configure = null, Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Configuration["Database:Path"] = bench.DatabasePath;
        builder.Configuration["Endpoints:Directory"] = bench.DatabasePath + ".interfaces";
        builder.Configuration["Endpoints:KeepFile"] = "false";
        builder.Configuration["Proxy:Directory"] = bench.DatabasePath + ".proxy";
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(bench.Options);
        builder.Services.AddSingleton<ITokenIssuer>(bench.Issuer);
        builder.Services.AddServerDatabase(bench.DatabasePath, bench.Options);
        builder.Services.AddSingleton(bench.GeoFiles);
        builder.Services.AddSingleton(PanelDefaults.Settings);
        builder.Services.AddSingleton(new WebOptions());
        builder.Services.AddOutbounds(builder.Configuration);
        builder.Services.AddEndpoints();
        builder.Services.AddClients(builder.Configuration);
        builder.Services.AddResolver();
        builder.Services.AddNames(builder.Configuration);
        builder.Services.AddAccessLog(builder.Configuration);
        builder.Services.AddFirewall();
        builder.Services.AddEndpointServices(builder.Configuration);
        builder.Services.AddSubscriptions();
        builder.Services.AddClientUpdates();
        builder.Services.AddSingleton<IHostNetwork>(new Ledger());
        builder.Services.AddSingleton<IHostCommands>(new Tools());
        builder.Services.AddSingleton<IAwgDevices>(new Kernel());
        builder.Services.RemoveAll<IHostedService>();
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1, Scopes.All.ToHashSet(), "admin");
            await next(context).ConfigureAwait(false);
        });
        app.MapConfigs();
        app.MapClients();
        configure?.Invoke(app);
        await app.StartAsync().ConfigureAwait(false);

        return app;
    }

    private static ConfigRequest Request(ServerConfig config) => new(config.Name, config.Host, config.ListenPort,
        [.. config.Address], [.. config.Dns], [.. config.AllowedIps], config.Mtu, config.Keepalive, false,
        config.Nat, false, [.. config.Blocked], config.PrivateKey, config.PresharedKey,
        ConfigAnswers.Obfuscation(config.Obfuscation, true), config.OfflineAfter, WebSocket: false, ServicesPort: config.ListenPort,
        WebSocketPath: config.WebSocketPath);
}
