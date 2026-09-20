using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Tests;

public class TemplateKindsTests
{
    [Fact]
    public async Task AFreshInstallCarriesOneTemplateOfEachKind()
    {
        using var bench = new Bench();

        var clients = await bench.Templates.ListAsync(CancellationToken.None);
        var interfaces = await bench.InterfaceTemplates.ListAsync(CancellationToken.None);
        var proxies = await bench.ProxyTemplates.ListAsync(CancellationToken.None);

        Assert.Equal(TemplateDefaults.Name, Assert.Single(clients).Name);
        Assert.Equal(InterfaceTemplateDefaults.Name, Assert.Single(interfaces).Name);
        Assert.Equal(ProxyTemplateDefaults.Name, Assert.Single(proxies).Name);
    }

    [Fact]
    public async Task TheBuiltInEndpointTemplateCarriesTheValuesOfAFreshEndpoint()
    {
        using var bench = new Bench();

        var template = Assert.Single(await bench.InterfaceTemplates.ListAsync(CancellationToken.None));

        Assert.Equal(ConfigDefaults.ListenPort, template.ListenPort);
        Assert.Equal(ConfigDefaults.Subnet, template.Subnet);
        Assert.Equal(ConfigDefaults.Mtu, template.Mtu);
        Assert.Equal(ConfigDefaults.Keepalive, template.Keepalive);
        Assert.Equal(ConfigDefaults.OfflineAfter, template.OfflineAfter);
        Assert.Equal(ConfigDefaults.Dns, template.Dns);
        Assert.Equal(ConfigDefaults.AllowedIps, template.AllowedIps);
        Assert.Equal(ConfigDefaults.Blocked, template.Blocked);
        Assert.Null(template.ClientTemplateId);
        Assert.Null(ConfigRules.CheckObfuscation(template.Obfuscation));
        Assert.Null(InterfaceTemplateRules.Check(template));
    }

    [Fact]
    public async Task TheBuiltInProxyTemplateListensOn443()
    {
        using var bench = new Bench();

        var template = Assert.Single(await bench.ProxyTemplates.ListAsync(CancellationToken.None));

        Assert.Equal(ProxyDefaults.Port, template.Port);
        Assert.Equal(ProxyKind.Ws, template.Kind);
        Assert.True(template.MakePath);
        Assert.Null(ProxyTemplateRules.Check(template));
    }

    [Fact]
    public void AnEndpointOfATemplateTakesItsConstantsAndKeepsItsOwnValues()
    {
        var template = InterfaceTemplateDefaults.Fresh() with
        {
            Id = 7,
            Dns = ["9.9.9.9"],
            Mtu = 1280,
            Keepalive = 15,
            Blocked = ["192.168.0.0/16"],
        };

        var config = template.Over(ConfigDefaults.Fresh("office") with
        {
            ListenPort = 51999,
            Address = ["10.9.1.1/24"],
            Host = "bor.sytes.net",
            Dns = ["1.1.1.1"],
            Mtu = 1420,
        });

        Assert.Equal(7, config.TemplateId);
        Assert.Equal(["9.9.9.9"], config.Dns);
        Assert.Equal(1280, config.Mtu);
        Assert.Equal(15, config.Keepalive);
        Assert.Equal(["192.168.0.0/16"], config.Blocked);
        Assert.Equal(template.Obfuscation, config.Obfuscation);
        Assert.Equal("office", config.Name);
        Assert.Equal("bor.sytes.net", config.Host);
        Assert.Equal(51999, config.ListenPort);
        Assert.Equal(["10.9.1.1/24"], config.Address);
    }

    [Fact]
    public void AFreshEndpointOfATemplateStartsFromItsPortAndSubnet()
    {
        var template = InterfaceTemplateDefaults.Fresh() with { ListenPort = 51888, Subnet = "10.77.0.1/24" };

        var config = template.Fresh("travel");

        Assert.Equal(51888, config.ListenPort);
        Assert.Equal(["10.77.0.1/24"], config.Address);
        Assert.Equal(template.Obfuscation, config.Obfuscation);
        Assert.Null(ConfigRules.Check(config));
    }

    [Fact]
    public async Task ChangingAnEndpointTemplateCarriesTheNewValuesIntoItsEndpoints()
    {
        using var bench = new Bench();
        var template = Assert.Single(await bench.InterfaceTemplates.ListAsync(CancellationToken.None));
        var added = await bench.Configs.AddAsync(template.Fresh("awg1"), CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        var changed = await bench.InterfaceTemplates.ChangeAsync(
            template.Id,
            template with { Mtu = 1300, Dns = ["8.8.8.8"] },
            CancellationToken.None);
        Assert.True(changed.IsOk, changed.Message);

        var carried = await bench.Configs.ChangeAsync(
            added.Record!.Id,
            changed.Record!.Over(added.Record!),
            CancellationToken.None);

        Assert.True(carried.IsOk, carried.Message);
        Assert.Equal(1300, carried.Record!.Mtu);
        Assert.Equal(["8.8.8.8"], carried.Record.Dns);
        Assert.Equal(template.Id, carried.Record.TemplateId);
        Assert.Equal("awg1", carried.Record.Name);
    }

    [Fact]
    public async Task AnEndpointTemplateAnEndpointTakesIsNotRemoved()
    {
        using var bench = new Bench();
        var template = Assert.Single(await bench.InterfaceTemplates.ListAsync(CancellationToken.None));
        await bench.Configs.AddAsync(template.Fresh("awg1"), CancellationToken.None);

        var gone = await bench.InterfaceTemplates.RemoveAsync(template.Id, CancellationToken.None);

        Assert.Equal(TemplateOutcome.InUse, gone.Outcome);
        Assert.Equal("template-in-use", gone.Code);
    }

    [Fact]
    public async Task AnEndpointWithoutATemplateIsGivenTheTemplateOfItsOwnValues()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        Assert.True(added.IsOk, added.Message);
        Assert.Null(added.Record!.TemplateId);

        await bench.InterfaceTemplates.SeedAsync(CancellationToken.None);

        var held = await bench.Configs.FindAsync(added.Record.Id, CancellationToken.None);
        Assert.NotNull(held!.TemplateId);
        var template = await bench.InterfaceTemplates.FindAsync(held.TemplateId!.Value, CancellationToken.None);
        Assert.Equal("awg1", template!.Name);
        Assert.Equal(added.Record.Obfuscation, template.Obfuscation);
        Assert.Equal(added.Record.Dns, template.Dns);
    }

    [Fact]
    public async Task TwoEndpointsUnderOneSetOfValuesShareOneTemplate()
    {
        using var bench = new Bench();
        var first = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), CancellationToken.None);
        var second = await bench.Configs.AddAsync(
            first.Record! with { Id = 0, Name = "awg2", ListenPort = 51821, Address = ["10.9.0.1/24"] },
            CancellationToken.None);
        Assert.True(second.IsOk, second.Message);

        await bench.InterfaceTemplates.SeedAsync(CancellationToken.None);

        var one = await bench.Configs.FindAsync(first.Record!.Id, CancellationToken.None);
        var other = await bench.Configs.FindAsync(second.Record!.Id, CancellationToken.None);
        Assert.Equal(one!.TemplateId, other!.TemplateId);
        Assert.Equal(2, (await bench.InterfaceTemplates.ListAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public void AProxyOfAWebsocketTemplateTakesAPathOfItsOwn()
    {
        var template = ProxyTemplateDefaults.Fresh() with { Id = 3, Opened = true };

        var one = template.Fresh("proxy0");
        var other = template.Fresh("proxy1");

        Assert.Equal(3, one.TemplateId);
        Assert.Equal(ProxyDefaults.Port, one.Port);
        Assert.True(one.Opened);
        Assert.NotEmpty(one.Path);
        Assert.NotEqual(one.Path, other.Path);
        Assert.Null(ProxyRules.Check(one));
    }

    [Fact]
    public void AProxyOfAWireguardTemplateCarriesATargetAndNoPath()
    {
        var template = ProxyTemplateDefaults.Fresh() with
        {
            Kind = ProxyKind.Wg,
            Port = 8080,
            Target = "127.0.0.1:51820",
        };

        var proxy = template.Fresh("relay0");

        Assert.Equal(ProxyKind.Wg, proxy.Kind);
        Assert.Equal(8080, proxy.Port);
        Assert.Equal("127.0.0.1:51820", proxy.Target);
        Assert.Empty(proxy.Path);
        Assert.Null(ProxyRules.Check(proxy));
    }

    [Fact]
    public async Task AProxyTemplateAProxyTakesIsNotRemoved()
    {
        using var bench = new Bench();
        var template = Assert.Single(await bench.ProxyTemplates.ListAsync(CancellationToken.None));
        var added = await bench.Proxies.AddAsync(template.Fresh("proxy0"), CancellationToken.None);
        Assert.True(added.IsOk, added.Message);

        var gone = await bench.ProxyTemplates.RemoveAsync(template.Id, CancellationToken.None);

        Assert.Equal(TemplateOutcome.InUse, gone.Outcome);
    }

    [Fact]
    public async Task ATemplateUnderATakenNameIsRefused()
    {
        using var bench = new Bench();
        var template = Assert.Single(await bench.InterfaceTemplates.ListAsync(CancellationToken.None));

        var again = await bench.InterfaceTemplates.AddAsync(
            InterfaceTemplateDefaults.Fresh() with { Name = template.Name },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task AnEndpointTemplateOutsideTheRulesIsRefused()
    {
        using var bench = new Bench();

        var refused = await bench.InterfaceTemplates.AddAsync(
            InterfaceTemplateDefaults.Fresh() with { Name = "over", ListenPort = 70000 },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.Invalid, refused.Outcome);
        Assert.Equal("bad-port", refused.Code);
    }

    [Fact]
    public async Task AnEndpointTemplateNamingAnUnknownClientTemplateIsRefused()
    {
        using var bench = new Bench();

        var refused = await bench.InterfaceTemplates.AddAsync(
            InterfaceTemplateDefaults.Fresh() with { Name = "other", ClientTemplateId = 4242 },
            CancellationToken.None);

        Assert.Equal(TemplateOutcome.Unknown, refused.Outcome);
        Assert.Equal("unknown-template", refused.Code);
    }
}
