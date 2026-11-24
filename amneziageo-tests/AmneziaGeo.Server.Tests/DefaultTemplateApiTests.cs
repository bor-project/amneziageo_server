using System.Net;
using System.Net.Http.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Balance;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Names;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class DefaultTemplateApiTests
{
    [Fact]
    public async Task TheApiSavesResetsAndProtectsTheDefaultName()
    {
        using var bench = new Bench();
        await using var app = Application(bench, Scopes.ManageClients, Scopes.ReadState);
        await app.StartAsync().ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var start = await api.GetFromJsonAsync<TemplateResponse>("/api/templates/default").ConfigureAwait(false);
        Assert.Equal(0, start!.Id);
        Assert.Empty((await api.GetFromJsonAsync<TemplateResponse[]>("/api/templates").ConfigureAwait(false))!);
        using var saved = await api.PutAsJsonAsync("/api/templates/default", new
        {
            name = "default", entries = new[] { "192.0.2.0/24" }, dns = new[] { "9.9.9.9" }, dnsMode = "own", mtu = 1280, keepalive = 15,
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var read = await api.GetFromJsonAsync<TemplateResponse>("/api/templates/default").ConfigureAwait(false);
        Assert.Equal(1280, read!.Mtu);
        Assert.Equal(["192.0.2.0/24"], read.AllowedIps);
        Assert.Equal(["9.9.9.9"], read.Dns);
        using var renamed = await api.PutAsJsonAsync("/api/templates/default", new { name = "changed" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        using var reset = await api.PostAsync("/api/templates/default/reset", null).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Null(await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false));
        Assert.Null((await api.GetFromJsonAsync<TemplateResponse>("/api/templates/default").ConfigureAwait(false))!.Mtu);
        await app.StopAsync().ConfigureAwait(false);
    }

    [Theory]
    [InlineData("", 401)]
    [InlineData(Scopes.ReadState, 403)]
    public async Task DefaultChangesRequireClientManagement(string scope, int status)
    {
        using var bench = new Bench();
        await using var app = Application(bench, scope);
        await app.StartAsync().ConfigureAwait(false);
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var saved = await api.PutAsJsonAsync("/api/templates/default", new { name = "default", mtu = 1280 }).ConfigureAwait(false);
        using var reset = await api.PostAsync("/api/templates/default/reset", null).ConfigureAwait(false);
        Assert.Equal((HttpStatusCode)status, saved.StatusCode);
        Assert.Equal((HttpStatusCode)status, reset.StatusCode);
        Assert.Null(await bench.Templates.EffectiveAsync(null, default).ConfigureAwait(false));
        await app.StopAsync().ConfigureAwait(false);
    }

    private static WebApplication Application(Bench bench, params string[] scopes)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new PanelSettings());
        builder.Services.AddSingleton(new WebOptions());
        builder.Services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        builder.Services.AddSingleton<IHostNetwork, Ledger>();
        builder.Services.AddSingleton<IAwgDevices, Kernel>();
        builder.Services.AddSingleton<OutboundHost>();
        builder.Services.AddSingleton<RoutePlans>();
        builder.Services.AddSingleton<BalanceLive>();
        builder.Services.AddSingleton<DnsState>();
        builder.Services.AddSingleton<DnsSets>();
        builder.Services.AddSingleton<StandInBook>();
        builder.Services.AddSingleton<AccessGate>();
        builder.Services.AddSingleton<NameGate>();
        builder.Services.AddScoped<RouteApplier>();
        builder.Services.AddScoped<TemplateRefresher>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (scopes.Any(scope => scope.Length > 0))
            {
                context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1,
                    scopes.ToHashSet(), "tester");
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapTemplates();
        return app;
    }
}
