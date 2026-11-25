using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Diagnostics;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Names;
using AmneziaGeo.Server.Api.Outbounds;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AmneziaGeo.Server.Tests;

public sealed class RoutingEndpointTests
{
    [Fact]
    public async Task RulesAndEntriesUseSeparateApisAndPreserveReferences()
    {
        using var bench = new Bench();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
        var retained = builder.Services.Where(one => one.ServiceType == typeof(IHostedService)).ToHashSet();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Configuration["Database:Path"] = bench.DatabasePath;
        builder.Services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        builder.Services.AddOutbounds(builder.Configuration);
        builder.Services.AddResolver();
        builder.Services.AddNames(builder.Configuration);
        builder.Services.AddAccessLog(builder.Configuration);
        builder.Services.AddSingleton<IHostNetwork>(new Ledger());
        builder.Services.AddSingleton<IHostCommands>(new Tools());
        builder.Services.AddSingleton<IAwgDevices>(new Kernel());
        foreach (var descriptor in builder.Services.Where(one => one.ServiceType == typeof(IHostedService) && !retained.Contains(one)).ToArray())
        {
            builder.Services.Remove(descriptor);
        }

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1, Scopes.All.ToHashSet(), "admin");
            await next(context).ConfigureAwait(false);
        });
        app.MapRules();
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            using var api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/api/") };
            var defaults = await api.GetFromJsonAsync<System.Text.Json.JsonElement>("traffic-rules/dns-defaults").ConfigureAwait(false);
            Assert.Equal(System.Text.Json.JsonValueKind.Array, defaults.GetProperty("servers").ValueKind);
            var fresh = await api.GetFromJsonAsync<TrafficRule>("traffic-rules/draft").ConfigureAwait(false);
            Assert.Equal(TrafficDnsMode.Default, fresh!.DnsMode);
            Assert.Empty(fresh.Dns);
            using var blank = await api.PostAsJsonAsync("traffic-rules", new TrafficRuleRequest("blank", [], [])).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            using var invalidMode = await api.PostAsJsonAsync("traffic-rules", new TrafficRuleRequest("mode", ["example.test"], [], DnsMode: "unknown")).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, invalidMode.StatusCode);
            using var createdRule = await api.PostAsJsonAsync("traffic-rules", new TrafficRuleRequest("video", ["192.0.2.0/24"], ["9.9.9.9"])).ConfigureAwait(false);
            Assert.True(createdRule.IsSuccessStatusCode, await createdRule.Content.ReadAsStringAsync().ConfigureAwait(false));
            var rule = (await createdRule.Content.ReadFromJsonAsync<TrafficRule>().ConfigureAwait(false))!;
            Assert.Equal(TrafficDnsMode.Custom, rule.DnsMode);
            Assert.Equal(["9.9.9.9"], rule.Dns);
            var request = new RouteRequest("drop video", true, RouteAction.Block, "", [rule.Id], ["443"], RouteProtocol.Tcp, Clients: ["alice"], Inbounds: ["awg0"]);
            using var createdRow = await api.PostAsJsonAsync("routing-entries", request).ConfigureAwait(false);
            Assert.True(createdRow.IsSuccessStatusCode, await createdRow.Content.ReadAsStringAsync().ConfigureAwait(false));
            var row = (await createdRow.Content.ReadFromJsonAsync<RouteResponse>().ConfigureAwait(false))!;
            Assert.Equal([rule.Id], row.RuleIds);
            Assert.Equal(["alice"], row.Clients);
            Assert.Equal(["awg0"], row.Inbounds);
            Assert.Equal(["443"], row.Ports);
            using var rowJson = JsonDocument.Parse(await createdRow.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.False(rowJson.RootElement.TryGetProperty("targets", out _));
            Assert.False(rowJson.RootElement.TryGetProperty("groups", out _));
            Assert.False(rowJson.RootElement.TryGetProperty("dns", out _));

            using var inUse = await api.DeleteAsync($"traffic-rules/{rule.Id}").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
            using var changedRule = await api.PutAsJsonAsync($"traffic-rules/{rule.Id}", new TrafficRuleRequest("video", ["198.51.100.0/24"], ["1.1.1.1"])).ConfigureAwait(false);
            changedRule.EnsureSuccessStatusCode();
            var held = await api.GetFromJsonAsync<RouteResponse>($"routing-entries/{row.Id}").ConfigureAwait(false);
            Assert.Equal(row.Id, held!.Id);
            Assert.Equal(row.Position, held.Position);
            Assert.Equal(row.RuleIds, held.RuleIds);
            using var noRules = await api.PostAsJsonAsync("routing-entries", request with { Name = "invalid", RuleIds = [] }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, noRules.StatusCode);
            using var unknown = await api.PostAsJsonAsync("routing-entries", request with { Name = "unknown", RuleIds = [long.MaxValue] }).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            foreach (var path in new[] { "rules", "address-groups" })
            {
                using var obsolete = await api.GetAsync(path).ConfigureAwait(false);
                Assert.Equal(HttpStatusCode.NotFound, obsolete.StatusCode);
            }

            using var deleted = await api.DeleteAsync($"routing-entries/{row.Id}").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using var removedRule = await api.DeleteAsync($"traffic-rules/{rule.Id}").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NoContent, removedRule.StatusCode);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }
}
