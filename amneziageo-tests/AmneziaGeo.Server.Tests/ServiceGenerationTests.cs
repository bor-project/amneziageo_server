using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public partial class ServiceTests
{
    [Fact]
    public async Task ModernWsRevokesAnAlreadyUpgradedConnectionWhenPolicyRestartFails()
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        using var front = new Front(held: true);
        var folder = Path.Combine(Path.GetTempPath(), "service-generation-" + Guid.NewGuid().ToString("N"));
        var runner = new GenerationRunner();
        var host = new ProxyHost(new Ledger(), runner, folder);
        try
        {
            var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("generation"), default).ConfigureAwait(false)).Record!;
            var client = (await bench.Clients.AddAsync(ClientDefaults.Fresh(config.Id, "client"), default).ConfigureAwait(false)).Record!;
            var store = new TransportProxyStore(bench.Db);
            var proxy = await store.SaveAsync(0, identity.Secure(new TransportProxyEntity
            {
                Name = "catalog", Host = "localhost", Port = 443, Path = "relay", IsEnabled = true,
            }), default).ConfigureAwait(false);
            var template = new TemplateEntity { Name = "selected", ProxyIds = proxy.Id.ToString() };
            bench.Db.Templates.Add(template);
            await bench.Db.SaveChangesAsync().ConfigureAwait(false);
            (await bench.Db.Clients.SingleAsync(one => one.Id == client.Id).ConfigureAwait(false)).TemplateId = template.Id;
            await bench.Db.SaveChangesAsync().ConfigureAwait(false);
            await host.ApplyAsync("catalog", front.Port, 0, "relay", default).ConfigureAwait(false);
            var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(), Life(),
                Options.Create(new JsonOptions()), NullLogger<ServiceDesk>.Instance, fronts: host);
            var endpoint = new ServiceEndpoint(0, "catalog", true, front.Port, 0, "relay") { ProxyId = proxy.Id };
            var point = new ServicePoint(443, [endpoint], "", "");
            var context = Upgrading("/relay");
            var upgrade = new ObservedUpgrade(new Silent());
            context.Features.Set<IHttpUpgradeFeature>(upgrade);
            context.Request.Headers["X-AmneziaGeo-Target"] = "127.0.0.1:51820";
            context.Request.Headers.Authorization = "Bearer " + TransportProxyStore.Credential(proxy, client.Id);
            var request = desk.AnswerAsync(context, point);
            await upgrade.Upgraded.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.False(request.IsCompleted);
            runner.Fail = true;
            var policy = new ProxyTargets(true, [new("127.0.0.1", 51820)]);
            var changed = await host.ApplyAsync("catalog", front.Port, 0, "relay", default, policy).ConfigureAwait(false);
            Assert.NotEmpty(changed.Message);
            await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.False(host.CanRelay("catalog"));
            Assert.True((await host.StateAsync("catalog", default).ConfigureAwait(false)).IsRunning);
            var next = Upgrading("/relay");
            next.Request.Headers["X-AmneziaGeo-Target"] = "127.0.0.1:51820";
            next.Request.Headers.Authorization = context.Request.Headers.Authorization;
            await desk.AnswerAsync(next, point).ConfigureAwait(false);
            Assert.Equal(503, next.Response.StatusCode);
        }
        finally
        {
            await host.WithdrawAsync("catalog", default).ConfigureAwait(false);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Theory]
    [InlineData("restricted")]
    [InlineData("disabled")]
    [InlineData("removed")]
    public async Task MigratedLegacyWsCannotBypassCatalogRestrictionOrKeepAnOldUnrestrictedFlow(string change)
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        using var front = new Front(held: true);
        var folder = Path.Combine(Path.GetTempPath(), "legacy-generation-" + Guid.NewGuid().ToString("N"));
        var runner = new GenerationRunner { Fail = true };
        var host = new ProxyHost(new Ledger(), runner, folder);
        try
        {
            var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("migrated"), default).ConfigureAwait(false)).Record!;
            var store = new TransportProxyStore(bench.Db);
            var proxy = await store.SaveAsync(0, identity.Secure(new TransportProxyEntity
            {
                Name = "migrated", Host = "localhost", Port = 443, Path = "relay", IsEnabled = true,
            }), default).ConfigureAwait(false);
            proxy.LegacyConfigId = config.Id;
            await bench.Db.SaveChangesAsync().ConfigureAwait(false);
            host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)> { [proxy.Id] = (null, 51820) });
            var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(), Life(),
                Options.Create(new JsonOptions()), NullLogger<ServiceDesk>.Instance, fronts: host);
            var endpoint = new ServiceEndpoint(config.Id, "legacy", true, front.Port, 51820, "relay") { PolicyProxyId = proxy.Id };
            var point = new ServicePoint(443, [endpoint], "", "");
            var context = Upgrading("/relay");
            var upgrade = new ObservedUpgrade(new Silent());
            context.Features.Set<IHttpUpgradeFeature>(upgrade);
            var request = desk.AnswerAsync(context, point);
            await upgrade.Upgraded.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.False(request.IsCompleted);
            var policy = new ProxyTargets(true, [new("external.example", 443)]);
            if (change == "removed")
            {
                await store.RemoveAsync(proxy.Id, default).ConfigureAwait(false);
            }
            else
            {
                proxy.TargetsJson = ProxyTargetRules.Write(policy);
                proxy.IsEnabled = change != "disabled";
                await store.SaveAsync(proxy.Id, proxy, default).ConfigureAwait(false);
            }

            host.ApplyLegacyPolicies(change == "restricted"
                ? new Dictionary<long, (ProxyTargets?, int)> { [proxy.Id] = (policy, 51820) } : []);
            await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            var next = Upgrading("/relay");
            next.Response.Body = new MemoryStream();
            await desk.AnswerAsync(next, point).ConfigureAwait(false);
            Assert.Equal(403, next.Response.StatusCode);
            Assert.Contains("proxy-target-denied", System.Text.Encoding.UTF8.GetString(((MemoryStream)next.Response.Body).ToArray()));
            if (change == "restricted")
            {
                proxy.TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("127.0.0.1", 51820)]));
                await store.SaveAsync(proxy.Id, proxy, default).ConfigureAwait(false);
                host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)>
                {
                    [proxy.Id] = (ProxyTargetRules.Read(proxy.TargetsJson), 51820),
                });
                using var recovered = new Front(held: false);
                var allowed = Upgrading("/relay");
                await desk.AnswerAsync(allowed, point with
                {
                    Endpoints = [endpoint with { Front = recovered.Port }],
                }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.NotEqual(403, allowed.Response.StatusCode);
                Assert.Contains("GET /relay", await recovered.Head.ConfigureAwait(false));
            }
        }
        finally
        {
            host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)>());
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public async Task SettleLinksMigratedLegacyPolicyAndExposesFaultOfPhysicallyRunningModernBackend()
    {
        using var bench = new Bench();
        using var identity = new ServiceTlsFixture();
        var port = Free();
        var config = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("migrated") with
        {
            Host = "localhost", ListenPort = port, WebSocket = true, WebSocketPath = "relay",
        }, default).ConfigureAwait(false)).Record!;
        var store = new TransportProxyStore(bench.Db);
        await store.MigrateLegacyAsync(default).ConfigureAwait(false);
        var proxy = Assert.Single(await store.ListAsync(default).ConfigureAwait(false));
        await store.SaveAsync(proxy.Id, identity.Secure(proxy), default).ConfigureAwait(false);
        var folder = Path.Combine(Path.GetTempPath(), "settle-generation-" + Guid.NewGuid().ToString("N"));
        var runner = new GenerationRunner();
        var host = new ProxyHost(new Ledger(), runner, folder);
        var share = new ServiceShare();
        var desk = new ServiceDesk(bench.Scopes, new SpeedTickets(), [], new SubscriptionState(), Life(),
            Options.Create(new JsonOptions()), NullLogger<ServiceDesk>.Instance, fronts: host);
        var services = new ServiceServer(bench.Scopes, desk, host, identity.Options,
            PanelDefaults.Settings with { Port = port }, share, NullLogger<ServiceServer>.Instance);
        try
        {
            Assert.Empty(await services.SettleAsync(default).ConfigureAwait(false));
            var initial = Assert.IsType<ServicePoint>(share.Point);
            var legacy = Assert.Single(initial.Endpoints.Where(endpoint => endpoint.ProxyId == 0));
            Assert.Equal(proxy.Id, legacy.PolicyProxyId);
            Assert.True(host.TryLegacyLease(proxy.Id, out var old));
            using var held = old;
            proxy.TargetsJson = ProxyTargetRules.Write(new ProxyTargets(true, [new("external.example", 443)]));
            await store.SaveAsync(proxy.Id, proxy, default).ConfigureAwait(false);
            runner.Fail = true;
            var faults = await services.SettleAsync(default).ConfigureAwait(false);
            Assert.Contains(-proxy.Id, faults.Keys);
            Assert.Contains("owned restart failure", faults[-proxy.Id]);
            Assert.True(old.Stopped.IsCancellationRequested);
            var next = Upgrading("/relay");
            next.Response.Body = new MemoryStream();
            await desk.AnswerAsync(next, Assert.IsType<ServicePoint>(share.Point)).ConfigureAwait(false);
            Assert.Equal(403, next.Response.StatusCode);
            Assert.Contains("proxy-target-denied", System.Text.Encoding.UTF8.GetString(((MemoryStream)next.Response.Body).ToArray()));
        }
        finally
        {
            host.ApplyLegacyPolicies(new Dictionary<long, (ProxyTargets?, int)>());
            await services.DisposeAsync().ConfigureAwait(false);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    private sealed class ObservedUpgrade(Stream stream) : IHttpUpgradeFeature
    {
        public TaskCompletionSource Upgraded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsUpgradableRequest => true;
        public Task<Stream> UpgradeAsync()
        {
            Upgraded.SetResult();
            return Task.FromResult(stream);
        }
    }

    private sealed class GenerationRunner : IProxyRunner
    {
        public bool Fail { get; set; }
        public Task<ProxyState> EnableAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Up);
        public Task<ProxyState> StateAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Up);
        public Task<ProxyState> StartAsync(string name, CancellationToken ct) =>
            Task.FromResult(Fail ? new ProxyState(true, "owned restart failure") : ProxyState.Up);
        public Task<ProxyState> StopAsync(string name, CancellationToken ct) => Task.FromResult(ProxyState.Down);
    }
}
