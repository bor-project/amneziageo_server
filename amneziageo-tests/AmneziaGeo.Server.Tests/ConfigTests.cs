using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Tests;

public class ConfigTests
{
    [Fact]
    public void AFreshEndpointHoldsTogether()
    {
        var config = ConfigDefaults.Fresh("awg1");

        Assert.Null(ConfigRules.Check(config));
        Assert.Equal(config.PublicKey, Curve25519.PublicOf(config.PrivateKey));
        Assert.Equal(ConfigDefaults.ListenPort, config.ListenPort);
        Assert.Equal([ConfigDefaults.Subnet], config.Address);
    }

    [Fact]
    public void AFreshEndpointCarriesObfuscationOfItsOwn()
    {
        var one = ConfigDefaults.Obfuscation();
        var other = ConfigDefaults.Obfuscation();

        var types = new[] { one.H1, one.H2, one.H3, one.H4 };
        Assert.Equal(4, types.Distinct(StringComparer.Ordinal).Count());
        Assert.All(types, type => Assert.True(AwgRange.TryParse(type, out var span) && !span.IsOne));
        Assert.All(types, type => { _ = AwgRange.TryParse(type, out var span); Assert.InRange((long)span.Low, (long)ConfigRules.LowestType, (long)ConfigRules.HighestType); });
        Assert.NotEqual(types, [other.H1, other.H2, other.H3, other.H4]);
        Assert.NotEqual(one.S1 + ConfigRules.HandshakeGap, one.S2);
    }

    [Theory]
    [InlineData("", "the name is empty")]
    [InlineData("Awg1", "lower case")]
    [InlineData("1awg", "lower case")]
    [InlineData("awg1234567890123", "longer than")]
    public void ANameOutsideTheShapeIsRefused(string name, string part)
    {
        Assert.Contains(part, ConfigRules.CheckName(name)!.Message);
    }

    [Fact]
    public void APortOutsideTheRangeIsRefused()
    {
        Assert.Contains("port", Check(config => config with { ListenPort = 0 }));
        Assert.Contains("port", Check(config => config with { ListenPort = 70000 }));
        Assert.Null(Check(config => config with { ListenPort = 65535 }));
    }

    [Fact]
    public void AnAddressThatIsNotARangeIsRefused()
    {
        Assert.Contains("not an address range", Check(config => config with { Address = ["10.8.0.1/64"] }));
        Assert.Contains("carries no address range", Check(config => config with { Address = [] }));
        Assert.Contains("not a name server", Check(config => config with { Dns = ["1.1.1.1/24"] }));
        Assert.Null(Check(config => config with { Address = ["10.8.0.1/24", "fd00::1/64"], Dns = [] }));
    }

    [Fact]
    public void APacketSizeOutsideTheRangeIsRefused()
    {
        Assert.Contains("packet size", Check(config => config with { Mtu = 100 }));
        Assert.Contains("packet size", Check(config => config with { Mtu = 20000 }));
        Assert.Null(Check(config => config with { Mtu = 0 }));
    }

    [Fact]
    public void AKeyThatIsNotThirtyTwoBytesIsRefused()
    {
        Assert.Contains("private key", Check(config => config with { PrivateKey = "short" }));
        Assert.Contains("private key", Check(config => config with { PrivateKey = string.Empty }));
    }

    [Fact]
    public void ObfuscationThatMakesPacketsAlikeIsRefused()
    {
        Assert.Contains("makes the two alike", Obfuscation(one => one with { S1 = 50, S2 = 50 + ConfigRules.HandshakeGap }));
        Assert.Contains("two packet types are the same", Obfuscation(one => one with { H1 = "100", H2 = "90-110" }));
        Assert.Contains("packet type is outside", Obfuscation(one => one with { H3 = "4" }));
        Assert.Contains("neither a number nor a span", Obfuscation(one => one with { H4 = "many" }));
        Assert.Contains("a timing is neither", Obfuscation(one => one with { RekeyTimeout = "5-" }));
        Assert.Contains("header protection key", Obfuscation(one => one with { HeaderProtectionKey = "short" }));
        Assert.Contains("junk packet count", Obfuscation(one => one with { Jc = 500 }));
        Assert.Contains("junk packet size", Obfuscation(one => one with { Jmin = 900, Jmax = 100 }));
        Assert.Contains("junk prepended", Obfuscation(one => one with { S3 = 5000 }));
    }

    [Fact]
    public async Task AnEndpointIsHeldAndGivenBack()
    {
        using var bench = new Bench();

        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        Assert.True(added.IsOk);
        var found = await bench.Configs.FindAsync(added.Record!.Id, default);
        Assert.Equal("awg1", found!.Name);
        Assert.Equal(added.Record.PrivateKey, found.PrivateKey);
        Assert.Equal([.. ConfigDefaults.AllowedIps], found.AllowedIps);
        Assert.Single(await bench.Configs.ListAsync(default));
    }

    [Fact]
    public async Task AnEndpointIsTurnedOffAndOnWithoutItsOtherSettingsMoving()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var off = await bench.Configs.SwitchAsync(added.Record!.Id, false, default);
        var on = await bench.Configs.SwitchAsync(added.Record.Id, true, default);

        Assert.False(off.Record!.IsEnabled);
        Assert.True(on.Record!.IsEnabled);
        Assert.Equal(added.Record.PrivateKey, on.Record.PrivateKey);
        Assert.Equal(added.Record.ListenPort, on.Record.ListenPort);
        Assert.Equal(ConfigOutcome.Unknown, (await bench.Configs.SwitchAsync(added.Record.Id + 100, true, default)).Outcome);
    }

    [Fact]
    public async Task TheWebSocketOfAnEndpointIsTurnedOffWithoutItsOtherSettingsMoving()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { WebSocket = true, ServicesPort = 8446 }, default);

        var off = await bench.Configs.WebSocketAsync(added.Record!.Id, false, default);

        Assert.False(off.Record!.WebSocket);
        Assert.False((await bench.Configs.FindAsync(added.Record.Id, default))!.WebSocket);
        Assert.True(off.Record.IsEnabled);
        Assert.Equal(8446, off.Record.ServicesPort);
        Assert.Equal(added.Record.PrivateKey, off.Record.PrivateKey);
        Assert.Equal(ConfigOutcome.Unknown, (await bench.Configs.WebSocketAsync(added.Record.Id + 100, false, default)).Outcome);
    }

    [Fact]
    public async Task AnEndpointTakesTheNameOfAnotherOnlyOnce()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var again = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        Assert.Equal(ConfigOutcome.NameTaken, again.Outcome);
    }

    [Fact]
    public async Task SettingsOutsideTheRulesDoNotReachTheDatabase()
    {
        using var bench = new Bench();

        var refused = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { ListenPort = 0 }, default);

        Assert.Equal(ConfigOutcome.Invalid, refused.Outcome);
        Assert.Empty(await bench.Configs.ListAsync(default));
    }

    [Fact]
    public async Task AChangedEndpointCarriesTheKeyThatBelongsToIt()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);
        var pair = Curve25519.Create();

        var changed = await bench.Configs.ChangeAsync(
            added.Record!.Id,
            added.Record with { Name = "awg2", ListenPort = 51821, PrivateKey = pair.PrivateKey, PublicKey = string.Empty },
            default);

        Assert.True(changed.IsOk);
        Assert.Equal("awg2", changed.Record!.Name);
        Assert.Equal(51821, changed.Record.ListenPort);
        Assert.Equal(pair.PublicKey, changed.Record.PublicKey);
        Assert.True(changed.Record.UpdatedUtc >= changed.Record.CreatedUtc);
    }

    [Fact]
    public async Task ARenamedEndpointCarriesItsNameIntoTheRules()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);
        var rule = await bench.Rules.AddAsync(
            RouteDefaults.Fresh("home") with { Action = RouteAction.Direct, Inbounds = ["awg1", "awg9"] },
            default);

        var changed = await bench.Configs.ChangeAsync(added.Record!.Id, added.Record with { Name = "awg2" }, default);

        Assert.True(changed.IsOk, changed.Message);
        Assert.Equal(["awg2", "awg9"], (await bench.Rules.FindAsync(rule.Record!.Id, default))!.Inbounds);
    }

    [Fact]
    public async Task AnEndpointThatIsGoneIsRefused()
    {
        using var bench = new Bench();

        Assert.Equal(ConfigOutcome.Unknown, (await bench.Configs.RemoveAsync(7, default)).Outcome);
        Assert.Equal(ConfigOutcome.Unknown, (await bench.Configs.ChangeAsync(7, ConfigDefaults.Fresh("awg1"), default)).Outcome);

        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);
        Assert.True((await bench.Configs.RemoveAsync(added.Record!.Id, default)).IsOk);
        Assert.Empty(await bench.Configs.ListAsync(default));
    }


    [Fact]
    public async Task TwoEndpointsDoNotListenOnOnePort()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var again = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg2"), default);

        Assert.Equal(ConfigOutcome.PortTaken, again.Outcome);
        Assert.Equal("port-taken", again.Code);
    }

    [Fact]
    public async Task TwoEndpointsServeOnOneServicesPort()
    {
        using var bench = new Bench();
        var first = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var second = await bench.Configs.AddAsync(
            ConfigDefaults.Fresh("awg2") with { ListenPort = 51821, ServicesPort = first.Record!.ListenPort },
            default);

        Assert.True(second.IsOk);
        Assert.Equal(first.Record.ListenPort, second.Record!.ServicesPort);
    }

    [Fact]
    public async Task AnEndpointLeavesThePortOfAPanelThatSitsAtTheRootAlone()
    {
        using var bench = new Bench();
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { Port = 8443, Path = string.Empty }, default);

        var taken = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { ServicesPort = 8443 }, default);
        await bench.Panel.SaveAsync(PanelDefaults.Settings with { Port = 8443, Path = "sub/l4kg8s0xq1zc7ab2" }, default);
        var shared = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { ServicesPort = 8443 }, default);

        Assert.Equal(ConfigOutcome.PortTaken, taken.Outcome);
        Assert.Equal("panel-path-needed", taken.Code);
        Assert.True(shared.IsOk);
        Assert.True(await bench.Configs.ServesAsync(8443, default));
        Assert.False(await bench.Configs.ServesAsync(8444, default));
    }

    [Fact]
    public async Task AnEndpointKeepsItsOwnPortThroughAChange()
    {
        using var bench = new Bench();
        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var changed = await bench.Configs.ChangeAsync(added.Record!.Id, added.Record with { Host = "bor.sytes.net" }, default);

        Assert.True(changed.IsOk);
        Assert.Equal("bor.sytes.net", changed.Record!.Host);
    }

    [Fact]
    public async Task AFreshEndpointGetsAPortNobodyListensOn()
    {
        using var bench = new Bench();
        await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1"), default);

        var port = await bench.Configs.FreePortAsync(ConfigDefaults.ListenPort, default);

        Assert.Equal(ConfigDefaults.ListenPort + 1, port);
    }

    [Fact]
    public async Task ARefusalNamesTheSettingBehindIt()
    {
        using var bench = new Bench();

        var refused = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { Mtu = 100 }, default);

        Assert.Equal("bad-mtu", refused.Code);
    }

    [Fact]
    public void APresharedKeyOutsideBase64IsRefused()
    {
        Assert.Null(Check(config => config with { PresharedKey = string.Empty }));
        Assert.Equal("bad-preshared", Fault(config => config with { PresharedKey = "short" }).Code);
    }

    [Fact]
    public async Task AnEndpointCarriesThePresharedKeyItWasGiven()
    {
        using var bench = new Bench();
        var key = Curve25519.Create().PrivateKey;

        var added = await bench.Configs.AddAsync(ConfigDefaults.Fresh("awg1") with { PresharedKey = key }, default);

        Assert.Equal(key, (await bench.Configs.FindAsync(added.Record!.Id, default))!.PresharedKey);
    }

    private static string? Check(Func<ServerConfig, ServerConfig> change) =>
        ConfigRules.Check(change(ConfigDefaults.Fresh("awg1")))?.Message;

    private static ConfigFault Fault(Func<ServerConfig, ServerConfig> change) =>
        ConfigRules.Check(change(ConfigDefaults.Fresh("awg1")))!;

    private static string Obfuscation(Func<ObfuscationSettings, ObfuscationSettings> change) =>
        ConfigRules.CheckObfuscation(change(ConfigDefaults.Obfuscation()))?.Message ?? string.Empty;
}
