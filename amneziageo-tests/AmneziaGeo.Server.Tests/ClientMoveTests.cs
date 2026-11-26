using System.Net;
using System.Net.Http.Json;
using AmneziaGeo.Server.Api.Clients;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AmneziaGeo.Server.Tests;

public sealed class ClientMoveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AClientKeepsItsSettingsAndSubscriptionWhenItMoves(bool refused)
    {
        using var bench = new Bench();
        var source = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg857a") with
        {
            Host = "source.example", ListenPort = 51820, ServicesPort = 30443, Address = ["10.57.0.1/24"],
        }, default).ConfigureAwait(false)).Record!;
        var target = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg857b") with
        {
            Host = "target.example", ListenPort = 51821, ServicesPort = 30444, Address = ["10.58.0.1/24", "fd58::1/64"],
        }, default).ConfigureAwait(false)).Record!;
        var template = (await bench.Templates.AddAsync(new ClientTemplate { Name = "moved-template" }, default).ConfigureAwait(false)).Record!;
        var draft = ClientDefaults.Fresh(source.Id, "move-phone") with
        {
            Address = ["10.57.0.23/32"], TemplateId = template.Id, Note = "keep this",
            PresharedKey = AmneziaGeo.Server.Core.Crypto.Curve25519.Create().PrivateKey, DailyLimit = 666,
            SubscriptionId = "move-phone-feed", Disconnect = false,
        };
        var made = (await bench.Clients.AddAsync(draft, default).ConfigureAwait(false)).Record!;
        var rule = await bench.Rules.AddAsync(RouteDefaults.Fresh("phone-route") with
        {
            Action = RouteAction.Direct, Targets = ["192.0.2.0/24"], Clients = [made.Name],
        }, default).ConfigureAwait(false);
        Assert.True(rule.IsOk, rule.Message);

        var devices = new MoveKernel(refused ? target.Name : null);
        var settings = SubscriptionDefaults.Settings with { IsEnabled = true, Path = "transfer-sub" };
        await using var app = await PlainWireGuardApiTests.CreateAsync(bench, devices: devices, configure: app =>
            app.MapGet("/transfer-sub/{**resource}", (HttpContext context) =>
                SubscriptionAnswer.WriteAsync(context, settings, app.Services.GetRequiredService<IServiceScopeFactory>()))).ConfigureAwait(false);
        var network = Assert.IsType<Ledger>(app.Services.GetRequiredService<IHostNetwork>());
        network.Links.UnionWith([source.Name, target.Name]);
        var tools = Assert.IsType<Tools>(app.Services.GetRequiredService<IHostCommands>());
        tools.Answers["tc -j"] = new CommandResult(0, "[]", string.Empty);
        foreach (var endpoint in new[] { source, target })
        {
            tools.Answers[$"ip -j -d link show dev {endpoint.Name}"] =
                new CommandResult(0, $"[{{\"ifname\":\"{endpoint.Name}\",\"mtu\":1420}}]", string.Empty);
        }
        tools.Answers["ip -j -d link show dev"] = new CommandResult(1, string.Empty, "Cannot find device");
        using var api = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var feed = $"/transfer-sub/{made.SubscriptionId}";
        var item = $"{feed}/{made.Id}";
        var previousFeed = await api.GetStringAsync(feed).ConfigureAwait(false);
        var previousItem = await api.GetStringAsync(item).ConfigureAwait(false);
        using var changed = await api.PutAsJsonAsync($"/api/clients/{made.Id}", new { ConfigId = target.Id }).ConfigureAwait(false);

        var details = changed.IsSuccessStatusCode ? string.Empty : await changed.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(refused ? details.Contains("client-move-refused", StringComparison.Ordinal) : changed.IsSuccessStatusCode, details);
        var stored = (await bench.Clients.FindAsync(made.Id, default).ConfigureAwait(false))!;
        Assert.Equal(refused ? source.Id : target.Id, stored.ConfigId);
        Assert.Equal(made.PrivateKey, stored.PrivateKey);
        Assert.Equal(made.PublicKey, stored.PublicKey);
        Assert.Equal(made.PresharedKey, stored.PresharedKey);
        Assert.Equal(made.Note, stored.Note);
        Assert.Equal(made.TemplateId, stored.TemplateId);
        Assert.Equal(made.DailyLimit, stored.DailyLimit);
        Assert.Equal(made.Disconnect, stored.Disconnect);
        Assert.Equal(made.SubscriptionId, stored.SubscriptionId);
        Assert.Equal([made.Name], (await bench.Rules.FindAsync(rule.Record!.Id, default).ConfigureAwait(false))!.Clients);

        var afterFeed = await api.GetStringAsync(feed).ConfigureAwait(false);
        var afterItem = await api.GetStringAsync(item).ConfigureAwait(false);
        var updates = devices.Updates.SelectMany(update => update.Peers
            .Where(peer => peer.PublicKey == made.PublicKey).Select(peer => (update.Name, peer.Remove))).ToArray();
        Assert.Contains((source.Name, true), updates);
        if (refused)
        {
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.Contains("client-move-refused", await changed.Content.ReadAsStringAsync().ConfigureAwait(false), StringComparison.Ordinal);
            Assert.Equal(made.Address, stored.Address);
            Assert.Equal(previousFeed, afterFeed);
            Assert.Equal(previousItem, afterItem);
            Assert.Equal((source.Name, false), updates[^1]);
        }
        else
        {
            changed.EnsureSuccessStatusCode();
            Assert.Equal(["10.58.0.2/32", "fd58::2/128"], stored.Address);
            Assert.NotEqual(previousFeed, afterFeed);
            Assert.NotEqual(previousItem, afterItem);
            Assert.Equal((target.Name, false), updates[^1]);
            var exported = (await api.GetFromJsonAsync<ClientConfigResponse>($"/api/clients/{made.Id}/config").ConfigureAwait(false))!;
            Assert.Contains("Endpoint = target.example:51821", exported.Text, StringComparison.Ordinal);
            Assert.Contains(target.PublicKey, exported.Text, StringComparison.Ordinal);
            Assert.Contains("fd58::2/128", exported.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnExhaustedTargetLeavesTheConfigurationAndSubscriptionAlone()
    {
        using var bench = new Bench();
        var source = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg857a") with { Address = ["10.57.0.1/24"] },
            default).ConfigureAwait(false)).Record!;
        var target = (await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg857b") with
        {
            Address = ["10.58.0.1/30"], ListenPort = source.ListenPort + 1,
        }, default).ConfigureAwait(false)).Record!;
        var held = await bench.Clients.AddAsync(ClientDefaults.Fresh(target.Id, "existing"), default).ConfigureAwait(false);
        Assert.True(held.IsOk, held.Message);
        var original = (await bench.Clients.AddAsync(ClientDefaults.Fresh(source.Id, "moving"), default).ConfigureAwait(false)).Record!;
        var move = await bench.Clients.ChangeAsync(original.Id, original with { ConfigId = target.Id, Address = [] }, default).ConfigureAwait(false);

        Assert.Equal("client-address-unavailable", move.Code);
        var found = (await bench.Clients.FindAsync(original.Id, default).ConfigureAwait(false))!;
        Assert.Equal(original.ConfigId, found.ConfigId);
        Assert.Equal(original.Address, found.Address);
        Assert.Equal(original.SubscriptionId, found.SubscriptionId);
    }

    [Fact]
    public void AFreeCompatibleAddressSurvivesANewSubnetworkForTheOtherFamily()
    {
        var selected = ClientPool.Relocate(["10.57.0.1/24", "fd58::1/64"], ["10.57.0.23/32", "fd57::23/128"],
            ["10.57.0.2/32", "fd58::2/128"]);
        Assert.Null(selected.Fault);
        Assert.Equal(["10.57.0.23/32", "fd58::3/128"], selected.Addresses);
    }

    private sealed class MoveKernel(string? refused) : IAwgDevices
    {
        private readonly Kernel _inside = new();
        public IReadOnlyList<AwgUpdate> Updates => _inside.Updates;
        public IReadOnlyList<string> Names() => _inside.Names();
        public AwgDevice? Find(string name) => _inside.Find(name);
        public IReadOnlyList<AwgDevice> List() => _inside.List();

        public void Apply(AwgUpdate update)
        {
            if (update.Name == refused && update.Peers.Any(peer => !peer.Remove))
            {
                throw new InvalidOperationException("the test kernel refused the new peer");
            }

            _inside.Apply(update);
        }
    }
}
