using System.Net;
using System.Net.Http.Json;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Rules;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class RoutingDefaultsApiTests
{
    [Theory]
    [InlineData("groups", "", 401)]
    [InlineData("groups", Scopes.ReadState, 403)]
    [InlineData("groups", Scopes.ManageClients, 403)]
    [InlineData("groups", Scopes.ManageRouting, 200)]
    [InlineData("presets", "", 401)]
    [InlineData("presets", Scopes.ReadState, 403)]
    [InlineData("presets", Scopes.ManageRouting, 403)]
    [InlineData("presets", Scopes.ManageClients, 200)]
    public async Task RestorationRequiresTheCollectionWriteScope(string part, string scope, int status)
    {
        using var bench = new Bench();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddServerDatabase(bench.DatabasePath, new AuthOptions());
        builder.Services.AddScoped<RouteApplier>();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (scope.Length > 0)
            {
                context.Items["amneziageo.caller"] = new Principal(1, "tester", AuthScheme.Password, 1,
                    new HashSet<string> { scope }, "tester");
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapAddressGroups();
        app.MapPresets();
        await app.StartAsync().ConfigureAwait(false);
        try
        {
            using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var path = part == "groups" ? "/api/address-groups/defaults" : "/api/templates/routing/defaults";
            using var response = await api.PostAsync(path, null).ConfigureAwait(false);
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            if (status == 200)
            {
                Assert.Equal(part == "groups" ? new RoutingDefaultsResult(7, 0) : new RoutingDefaultsResult(0, 7),
                    await response.Content.ReadFromJsonAsync<RoutingDefaultsResult>().ConfigureAwait(false));
                using var repeated = await api.PostAsync(path, null).ConfigureAwait(false);
                Assert.Equal(new RoutingDefaultsResult(0, 0),
                    await repeated.Content.ReadFromJsonAsync<RoutingDefaultsResult>().ConfigureAwait(false));
            }
            else
            {
                Assert.Empty(await bench.Presets.ListAsync(default).ConfigureAwait(false));
                Assert.Empty(await new AddressGroupStore(bench.Db).ListAsync(default).ConfigureAwait(false));
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }
}
