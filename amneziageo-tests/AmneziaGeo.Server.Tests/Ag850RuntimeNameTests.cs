using System.Text;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.Traffic;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class Ag850RuntimeNameTests
{
    [Theory]
    [InlineData("awg0")]
    [InlineData("transport-proxy-1")]
    [InlineData("panel-sytes-net")]
    public void ExistingProxyIdentitiesRemainUnchanged(string name)
    {
        Assert.Equal(name, ProxyFile.Identity(name));
        Assert.Equal($"proxy-{name}.env", ProxyFile.Arguments(name));
        Assert.Equal($"proxy-{name}.yaml", ProxyFile.Rules(name));
        Assert.Equal(name, ProxyFile.Name(ProxyFile.Arguments(name)));
        Assert.Equal($"{ProxyDefaults.Service}@{name}", ProxyHost.Unit(name));
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("Bor.example")]
    [InlineData("Москва")]
    [InlineData("+node")]
    [InlineData("@node")]
    [InlineData("-node")]
    [InlineData("_4142")]
    public async Task ExpandedNamesUseOneIdentityForFilesUnitsAndDiscovery(string name)
    {
        var folder = Directory.CreateTempSubdirectory("ag850-runtime-");
        try
        {
            var commands = new Commands();
            var host = new ProxyHost(new Ledger(), new SystemdProxies(commands), folder.FullName);
            var identity = ProxyFile.Identity(name);
            Assert.StartsWith("_", identity, StringComparison.Ordinal);
            Assert.All(identity, one => Assert.True(char.IsAsciiLetterOrDigit(one) || one == '_'));
            Assert.NotEqual(name, identity);
            var result = await host.ApplyAsync(name, 61001, 51820, "preview", default).ConfigureAwait(false);
            Assert.True(result.IsRunning, result.Message);
            Assert.True(File.Exists(Path.Combine(folder.FullName, $"proxy-{identity}.yaml")));
            var arguments = await File.ReadAllTextAsync(Path.Combine(folder.FullName, $"proxy-{identity}.env")).ConfigureAwait(false);
            Assert.Equal(Path.Combine(folder.FullName, $"proxy-{identity}.yaml"), ChildProxies.Arguments(arguments)[2]);
            Assert.Equal(name, Assert.Single(host.Held()));
            Assert.Contains(commands.Calls, call => call.File == "systemctl" && call.Arguments.SequenceEqual(new[] { "enable", $"{ProxyDefaults.Service}@{identity}" }));
            Assert.Contains(commands.Calls, call => call.File == "systemctl" && call.Arguments.SequenceEqual(new[] { "restart", $"{ProxyDefaults.Service}@{identity}" }));
            Assert.Contains(commands.Calls, call => call.File == "systemctl" && call.Arguments.Contains($"{ProxyDefaults.Service}@{identity}") && call.Arguments[0] == "show");
            await host.WithdrawAsync(name, default).ConfigureAwait(false);
            Assert.Empty(host.Held());
            Assert.Contains(commands.Calls, call => call.Arguments.SequenceEqual(new[] { "disable", "--now", $"{ProxyDefaults.RelayService}@{identity}" }));
            Assert.Contains(commands.Calls, call => call.Arguments.SequenceEqual(new[] { "disable", "--now", $"{ProxyDefaults.Service}@{identity}" }));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void EncodedNamesDoNotCollideWithLegacyOrLiteralEncodedNames()
    {
        Assert.NotEqual(ProxyFile.Identity("AB"), ProxyFile.Identity("ab"));
        Assert.NotEqual(ProxyFile.Identity("AB"), ProxyFile.Identity("_4142"));
        Assert.Equal("AB", ProxyFile.Name(ProxyFile.Arguments("AB")));
        Assert.Equal("_4142", ProxyFile.Name(ProxyFile.Arguments("_4142")));
    }

    [Theory]
    [InlineData("proxy-_.env")]
    [InlineData("proxy-_a.env")]
    [InlineData("proxy-_zz.env")]
    [InlineData("proxy-_ff.env")]
    [InlineData("proxy-_00.env")]
    [InlineData("proxy-_61776730.env")]
    public void MalformedOrNoncanonicalEncodedFilesAreNotDiscovered(string file)
    {
        Assert.Null(ProxyFile.Name(file));
    }

    [Fact]
    public void ExistingUdpPrivacyTableKeepsItsIdentityAndText()
    {
        Assert.Equal("amneziageo_udp_awg0", UdpPrivacyRuleset.Table("awg0"));
        Assert.Equal("table inet amneziageo_udp_awg0\ndelete table inet amneziageo_udp_awg0\n", UdpPrivacyRuleset.Clear("awg0"));
        Assert.Contains("table inet amneziageo_udp_awg0 {", UdpPrivacyRuleset.Text("awg0", 51820), StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedUdpTablesDoNotCollideWithRawOrLiteralEncodedNames()
    {
        var names = new[] { "ab", "AB", "_4142", "a-1", "a_1", "Москва" };
        Assert.Equal(names.Length, names.Select(UdpPrivacyRuleset.Table).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("amneziageo_udp_ab", UdpPrivacyRuleset.Table("ab"));
        Assert.Equal("amneziageo_udp__4142", UdpPrivacyRuleset.Table("AB"));
        Assert.NotEqual(UdpPrivacyRuleset.Table("AB"), UdpPrivacyRuleset.Table("_4142"));
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("Bor.example")]
    [InlineData("Москва")]
    [InlineData("+node")]
    [InlineData("@node")]
    [InlineData("-node")]
    public void ExpandedUdpPrivacyTablesUseAsciiIdentities(string name)
    {
        var table = "amneziageo_udp__" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(name));
        Assert.Equal(table, UdpPrivacyRuleset.Table(name));
        Assert.Equal($"table inet {table}\ndelete table inet {table}\n", UdpPrivacyRuleset.Clear(name));
        Assert.Contains($"table inet {table} {{", UdpPrivacyRuleset.Text(name, 51820), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("Москва")]
    [InlineData("+node")]
    [InlineData("@node")]
    public async Task ExtendedUdpTablesUseTheirAsciiIdentityWhenReadThroughNft(string name)
    {
        var commands = new Commands();
        var table = UdpPrivacyRuleset.Table(name);
        await new IpHostNetwork(commands).ReadFirewallAsync(table, default).ConfigureAwait(false);
        var call = Assert.Single(commands.Calls);
        Assert.Equal("nft", call.File);
        Assert.Equal(new[] { "-j", "list", "table", "inet", table }, call.Arguments);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("dev")]
    [InlineData("name")]
    [InlineData("-node")]
    [InlineData("Москва")]
    public async Task IpCommandsKeepTheNameInAnExplicitArgument(string name)
    {
        var commands = new Commands();
        var network = new IpHostNetwork(commands);
        await network.AddLinkAsync(name, default).ConfigureAwait(false);
        await network.RemoveLinkAsync(name, default).ConfigureAwait(false);
        Assert.Equal("ip", commands.Calls[0].File);
        Assert.Equal(new[] { "link", "add", "name", name, "type", "amneziawg" }, commands.Calls[0].Arguments);
        Assert.Equal(new[] { "link", "del", "dev", name }, commands.Calls[1].Arguments);
    }

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("Bor.example")]
    [InlineData("Москва")]
    [InlineData("+node")]
    [InlineData("@node")]
    [InlineData("-node")]
    public void BandwidthPlansAcceptTheSameInterfaceNames(string name)
    {
        Assert.Null(ConfigRules.CheckName(name));
        var plan = BandwidthPlan.Create(name, [new ClientBandwidth(1, ["10.85.0.2/32"], 1_000_000, 2_000_000)]);
        Assert.Equal(name, plan.Interface);
        Assert.True(plan.Upload.IsLimited);
        Assert.True(plan.Download.IsLimited);
        Assert.All(plan.Ifb, one => Assert.True(char.IsAsciiLetterOrDigit(one)));
    }

    private sealed class Commands : IHostCommands
    {
        public List<(string File, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, string? input, CancellationToken ct)
        {
            Calls.Add((file, arguments.ToArray()));
            return Task.FromResult(new CommandResult(0, "ActiveState=active\nSubState=running\nNRestarts=0\nExecMainStatus=0\n", string.Empty));
        }
    }
}
