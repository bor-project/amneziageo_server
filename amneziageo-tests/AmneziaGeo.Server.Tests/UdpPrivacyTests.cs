using System.Text.Json;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AmneziaGeo.Server.Tests;

public class UdpPrivacyTests
{
    [Fact]
    public void OldRequestsKeepTheHeldPrivacyAndNewRequestsRemainPublic()
    {
        var request = JsonSerializer.Deserialize<ConfigRequest>("{}")!;
        var held = ConfigDefaults.Fresh("awg0") with { UdpPrivate = true };

        Assert.False(ConfigAnswers.Draft(request).UdpPrivate);
        Assert.True(ConfigAnswers.Draft(request, held).UdpPrivate);
        Assert.False(ConfigAnswers.Draft(request with { UdpPrivate = false }, held).UdpPrivate);
        Assert.True(ConfigAnswers.Config(held, false).UdpPrivate);
    }

    [Fact]
    public async Task PrivacySurvivesStorageAndOtherEndpointChanges()
    {
        using var bench = new Bench();
        var draft = ConfigDefaults.Fresh("awg0") with { UdpPrivate = true };
        var added = await bench.Configs.AddAsync(draft, default).ConfigureAwait(false);
        Assert.True(added.IsOk);
        var id = added.Record!.Id;
        bench.Db.ChangeTracker.Clear();
        Assert.True((await bench.Configs.FindAsync(id, default).ConfigureAwait(false))!.UdpPrivate);
        var changed = await bench.Configs.OpenedAsync(id, true, default).ConfigureAwait(false);
        Assert.True(changed.Record!.UdpPrivate);
        await bench.Configs.SwitchAsync(id, false, default).ConfigureAwait(false);
        Assert.True((await bench.Configs.FindAsync(id, default).ConfigureAwait(false))!.UdpPrivate);
        Assert.True(await bench.Db.Configs.AsNoTracking().AnyAsync(one => one.Id == id && one.UdpPrivate).ConfigureAwait(false));
    }

    [Fact]
    public async Task AnExistingDatabaseKeepsItsEndpointsPublicAfterUpgrade()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg0"), default).ConfigureAwait(false);
        var held = added.Record!;
        bench.Db.ChangeTracker.Clear();
        var migrator = bench.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261104190933_ProxySourceIdentity").ConfigureAwait(false);
        await migrator.MigrateAsync().ConfigureAwait(false);

        var restored = (await bench.Configs.FindAsync(held.Id, default).ConfigureAwait(false))!;

        Assert.False(restored.UdpPrivate);
        Assert.Equal(held.PrivateKey, restored.PrivateKey);
        Assert.Equal(held.ListenPort, restored.ListenPort);
        Assert.Equal(held.Address, restored.Address);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrivateEndpointsDoNotOpenUdpEvenWhenProxyTcpIsShared(bool websocket)
    {
        var config = ConfigDefaults.Fresh("awg0") with { Opened = true, UdpPrivate = true, WebSocket = websocket, ServicesPort = 443 };
        var plan = FirewallPlan.Of([config], new PanelSettings { Port = 443, Opened = true }, new SubscriptionSettings());

        Assert.DoesNotContain(plan.Ports, one => one.Protocol == "udp" && one.Port == config.ListenPort);
        Assert.Single(plan.Ports, one => one.Protocol == "tcp" && one.Port == 443);
        Assert.Contains(config.Name, plan.Interfaces);
    }

    [Fact]
    public async Task PrivateProtectionPrecedesKernelPortAndInterfaceStartup()
    {
        var ledger = new Ledger();
        var kernel = new Kernel();
        var config = ConfigDefaults.Fresh("awg0") with { UdpPrivate = true };

        var sync = await new EndpointHost(ledger, kernel).ApplyAsync(config, default).ConfigureAwait(false);

        Assert.True(sync.IsDone);
        Assert.Equal("firewall", ledger.Steps[0]);
        Assert.Contains("iifname != \"lo\" udp dport 51820 counter drop", ledger.Ruleset);
        Assert.Equal(config.ListenPort, (int)Assert.Single(kernel.Updates).ListenPort!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARefusedGuardDoesNotLeaveAnExposedInterface(bool existed)
    {
        var ledger = new Ledger { Refuses = "firewall" };
        if (existed)
        {
            ledger.Links.Add("awg0");
        }

        var kernel = new Kernel();
        var sync = await new EndpointHost(ledger, kernel)
            .ApplyAsync(ConfigDefaults.Fresh("awg0") with { UdpPrivate = true }, default).ConfigureAwait(false);

        Assert.False(sync.IsDone);
        Assert.Equal(EndpointFault.Failed, sync.Fault);
        Assert.Empty(kernel.Updates);
        Assert.DoesNotContain("awg0", ledger.Links);
        Assert.DoesNotContain(ledger.Steps, one => one.StartsWith("up ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReturningToPublicOrRemovingAnEndpointClearsOnlyItsGuard(bool enabled)
    {
        var ledger = new Ledger();
        ledger.Links.Add("awg0");
        ledger.Tables[UdpPrivacyRuleset.Table("awg0")] = "held";
        var config = ConfigDefaults.Fresh("awg0") with { IsEnabled = enabled };

        var sync = await new EndpointHost(ledger, new Kernel()).ApplyAsync(config, default).ConfigureAwait(false);

        Assert.True(sync.IsDone);
        Assert.Equal(UdpPrivacyRuleset.Clear("awg0"), ledger.Ruleset);
        Assert.DoesNotContain("amneziageo_udp_awg1", ledger.Ruleset);
        if (!enabled)
        {
            Assert.True(ledger.Steps.IndexOf("remove awg0") < ledger.Steps.IndexOf("firewall"));
        }
        else
        {
            Assert.True(ledger.Steps.FindIndex(one => one.StartsWith("up awg0", StringComparison.Ordinal)) < ledger.Steps.IndexOf("firewall"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AGuardThatCouldNotBeReadDoesNotProduceASuccessfulPublicOrDisabledState(bool enabled)
    {
        var ledger = new Ledger { Refuses = "read" };
        ledger.Links.Add("awg0");
        ledger.Tables[UdpPrivacyRuleset.Table("awg0")] = "held";

        var sync = await new EndpointHost(ledger, new Kernel())
            .ApplyAsync(ConfigDefaults.Fresh("awg0") with { IsEnabled = enabled }, default).ConfigureAwait(false);

        Assert.False(sync.IsDone);
        Assert.Contains("read amneziageo_udp_awg0", sync.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("firewall", ledger.Steps);
        Assert.True(ledger.Tables.ContainsKey(UdpPrivacyRuleset.Table("awg0")));
    }

    [Fact]
    public async Task MovingAPrivatePortStopsTheOldSocketBeforeReplacingItsGuard()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awg0");
        var config = ConfigDefaults.Fresh("awg0") with { UdpPrivate = true, ListenPort = 51821 };
        var kernel = new Kernel();
        kernel.Hold(new AwgDevice { Name = config.Name, ListenPort = 51820, Obfuscation = ConfigDevice.Obfuscation(config.Obfuscation) });

        var sync = await new EndpointHost(ledger, kernel).ApplyAsync(config, default).ConfigureAwait(false);

        Assert.True(sync.IsDone);
        Assert.Equal(["remove awg0", "firewall", "forwarding", "add awg0"], ledger.Steps.Take(4));
        Assert.Contains("udp dport 51821 counter drop", ledger.Ruleset);
    }

    [Fact]
    public async Task APrivateEndpointWithTheSamePortKeepsItsInterfaceAndPeers()
    {
        var ledger = new Ledger();
        ledger.Links.Add("awg0");
        var config = ConfigDefaults.Fresh("awg0") with { UdpPrivate = true };
        var kernel = new Kernel();
        kernel.Hold(new AwgDevice { Name = config.Name, ListenPort = (ushort)config.ListenPort, Obfuscation = ConfigDevice.Obfuscation(config.Obfuscation) });

        var sync = await new EndpointHost(ledger, kernel).ApplyAsync(config, default).ConfigureAwait(false);

        Assert.True(sync.IsDone);
        Assert.DoesNotContain("remove awg0", ledger.Steps);
        Assert.DoesNotContain("add awg0", ledger.Steps);
    }

    [Theory]
    [InlineData("awg0", 1)]
    [InlineData("awg-old", 65535)]
    public void AGuardCoversBothIpFamiliesAndEveryNonLoopbackInterface(string name, int port)
    {
        var text = UdpPrivacyRuleset.Text(name, port);

        Assert.Contains("table inet", text);
        Assert.Contains($"iifname != \"lo\" udp dport {port} counter drop", text);
        Assert.DoesNotContain("ct state", text);
        Assert.DoesNotContain("tcp dport", text);
    }

    [Theory]
    [InlineData("awg\"0", 51820)]
    [InlineData("awg0", 0)]
    [InlineData("awg0", 65536)]
    public void InvalidNamesAndPortsAreRefused(string name, int port)
    {
        Assert.ThrowsAny<ArgumentException>(() => UdpPrivacyRuleset.Text(name, port));
    }
}
