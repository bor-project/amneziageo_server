using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Tests;

public class ProxyTests
{
    [Fact]
    public void AFreshProxyTakesTheHttpsPortAndAPathNoOneGuesses()
    {
        var proxy = ProxyDefaults.Fresh("proxy0");

        Assert.Equal("proxy0", proxy.Name);
        Assert.Equal(443, proxy.Port);
        Assert.False(proxy.IsEnabled);
        Assert.True(proxy.Path.Length >= 20);
        Assert.NotEqual(proxy.Path, ProxyDefaults.Fresh("proxy1").Path);
        Assert.Null(ProxyRules.Check(proxy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("proxy/one")]
    [InlineData("a name")]
    [InlineData("proxy0proxy0proxy0")]
    public void ANameTheServiceCannotCarryIsRefused(string name)
    {
        Assert.Equal("bad-name", ProxyRules.Check(Fresh() with { Name = name })?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void APortOutsideTheRangeIsRefused(int port)
    {
        Assert.Equal("bad-port", ProxyRules.Check(Fresh() with { Port = port })?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("../secret")]
    [InlineData("with space")]
    public void APathThatTheProxyCannotServeIsRefused(string path)
    {
        Assert.Equal("bad-path", ProxyRules.Check(Fresh() with { Path = path })?.Code);
    }

    [Fact]
    public void ACertificateTakesBothPathsAndStartsAtTheRoot()
    {
        var half = Fresh() with { Certificate = "/tls/chain.pem" };
        var whole = half with { CertificateKey = "/tls/key.pem" };
        var loose = whole with { Certificate = "tls/chain.pem" };

        Assert.Equal("bad-certificate", ProxyRules.Check(half)?.Code);
        Assert.Equal("bad-certificate", ProxyRules.Check(loose)?.Code);
        Assert.Null(ProxyRules.Check(whole));
    }

    [Fact]
    public void AProxyTurnedOnWithoutACertificateIsRefused()
    {
        var on = Fresh() with { IsEnabled = true };

        Assert.Equal("no-certificate", ProxyRules.CheckReady(on, string.Empty, string.Empty)?.Code);
        Assert.Null(ProxyRules.CheckReady(on, "/tls/chain.pem", "/tls/key.pem"));
        Assert.Null(ProxyRules.CheckReady(Fresh(), string.Empty, string.Empty));
    }

    [Fact]
    public void TheWhitelistNamesThePortsOfTheHostAndTheLoopbackAlone()
    {
        var text = ProxyFile.Whitelist("v1", [51820, 443, 51820]);

        Assert.Contains("!PathPrefix '^v1$'", text, StringComparison.Ordinal);
        Assert.Contains("- Udp", text, StringComparison.Ordinal);
        Assert.Contains("- 443", text, StringComparison.Ordinal);
        Assert.Contains("127.0.0.0/8", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0/0", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "- 51820"));
    }

    [Fact]
    public void AHostWithNoInterfacesLetsNothingThrough()
    {
        Assert.Equal("restrictions: []\n", ProxyFile.Whitelist("v1", []));
        Assert.Equal("restrictions: []\n", ProxyFile.Whitelist(string.Empty, [51820]));
    }

    [Fact]
    public void TheArgumentsCarryTheCertificateAndTheWhitelist()
    {
        var line = ProxyFile.Line(Fresh() with { Port = 8443 }, "/tls/chain.pem", "/tls/key.pem", "/etc/proxy-a.yaml");

        Assert.StartsWith("PROXY_ARGS=wss://0.0.0.0:8443", line, StringComparison.Ordinal);
        Assert.Contains("--restrict-config /etc/proxy-a.yaml", line, StringComparison.Ordinal);
        Assert.Contains("--tls-certificate /tls/chain.pem", line, StringComparison.Ordinal);
        Assert.Contains("--tls-private-key /tls/key.pem", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheArgumentsGoPlainWhenNoCertificateIsNamed()
    {
        var line = ProxyFile.Line(Fresh(), string.Empty, string.Empty, "/etc/proxy-a.yaml");

        Assert.StartsWith("PROXY_ARGS=ws://0.0.0.0:443", line, StringComparison.Ordinal);
        Assert.DoesNotContain("--tls-certificate", line, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryProxyCarriesFilesAndAServiceOfItsOwn()
    {
        var host = new ProxyHost(new Tools(), new Ledger(), "/etc/amneziageo-server");

        Assert.Equal("/etc/amneziageo-server/proxy-a.yaml", host.RulesPath("a"));
        Assert.Equal("/etc/amneziageo-server/proxy-b.env", host.ArgumentsPath("b"));
        Assert.Equal("amneziageo-proxy@a", ProxyHost.Unit("a"));
        Assert.Equal("amneziageo-proxy@a", ProxyHost.Unit("a", ProxyKind.Ws));
        Assert.Equal("amneziageo-relay@a", ProxyHost.Unit("a", ProxyKind.Wg));
    }

    [Fact]
    public async Task TurningAProxyOnWritesItsFilesAndStartsItsService()
    {
        var tools = new Tools();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, new Ledger(), directory);

        try
        {
            var state = await host.ApplyAsync(
                Fresh() with { IsEnabled = true, Path = "v1" },
                [51820],
                "/tls/chain.pem",
                "/tls/key.pem",
                CancellationToken.None);

            Assert.True(state.IsRunning);
            Assert.True(tools.Called("systemctl enable amneziageo-proxy@proxy0"));
            Assert.True(tools.Called("systemctl restart amneziageo-proxy@proxy0"));
            Assert.Contains("- 51820", await File.ReadAllTextAsync(host.RulesPath("proxy0")), StringComparison.Ordinal);
            Assert.Contains(
                "--restrict-config " + host.RulesPath("proxy0"),
                await File.ReadAllTextAsync(host.ArgumentsPath("proxy0")),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TurningAProxyOffTakesItsServiceDownAndLeavesTheFiles()
    {
        var tools = new Tools();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, new Ledger(), directory);

        var state = await host.ApplyAsync(Fresh(), [], string.Empty, string.Empty, CancellationToken.None);

        Assert.False(state.IsRunning);
        Assert.True(tools.Called("systemctl disable --now amneziageo-proxy@proxy0"));
        Assert.False(tools.Called("systemctl restart"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task AProxyTakenAwayLeavesNoFilesBehind()
    {
        var tools = new Tools();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, new Ledger(), directory);

        try
        {
            await host.ApplyAsync(
                Fresh() with { IsEnabled = true },
                [51820],
                "/tls/chain.pem",
                "/tls/key.pem",
                CancellationToken.None);

            await host.WithdrawAsync("proxy0", ProxyKind.Ws, CancellationToken.None);

            Assert.True(tools.Called("systemctl disable --now amneziageo-proxy@proxy0"));
            Assert.False(File.Exists(host.RulesPath("proxy0")));
            Assert.False(File.Exists(host.ArgumentsPath("proxy0")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AServiceThatDoesNotComeUpSaysWhy()
    {
        var tools = new Tools();
        tools.Answers["systemctl restart"] = new CommandResult(1, string.Empty, "port 443 is taken");
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, new Ledger(), directory);

        try
        {
            var state = await host.ApplyAsync(
                Fresh() with { IsEnabled = true },
                [51820],
                "/tls/chain.pem",
                "/tls/key.pem",
                CancellationToken.None);

            Assert.False(state.IsRunning);
            Assert.Contains("port 443 is taken", state.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AFreshWireguardProxyCarriesATargetAndNoPath()
    {
        var proxy = ProxyDefaults.Fresh("proxy0", ProxyKind.Wg) with { Target = "127.0.0.1:51820" };

        Assert.Equal(ProxyKind.Wg, proxy.Kind);
        Assert.Empty(proxy.Path);
        Assert.Null(ProxyRules.Check(proxy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http")]
    [InlineData("WG")]
    public void AKindThePanelDoesNotKnowIsRefused(string kind)
    {
        Assert.Equal("bad-kind", ProxyRules.Check(Fresh() with { Kind = kind })?.Code);
    }

    [Fact]
    public void WhatOneKindCarriesTheOtherRefuses()
    {
        var relay = ProxyDefaults.Fresh("proxy0", ProxyKind.Wg);

        Assert.Equal("bad-target", ProxyRules.Check(relay)?.Code);
        Assert.Equal("bad-path", ProxyRules.Check(relay with { Target = "127.0.0.1:51820", Path = "v1" })?.Code);
        Assert.Equal("bad-target", ProxyRules.Check(Fresh() with { Target = "127.0.0.1:51820" })?.Code);
        Assert.Equal(
            "bad-certificate",
            ProxyRules.Check(relay with
            {
                Target = "127.0.0.1:51820",
                Certificate = "/tls/chain.pem",
                CertificateKey = "/tls/key.pem",
            })?.Code);
    }

    [Theory]
    [InlineData("127.0.0.1:51820", "127.0.0.1", 51820)]
    [InlineData("proxy.example.net:443", "proxy.example.net", 443)]
    [InlineData("[2001:db8::1]:51820", "2001:db8::1", 51820)]
    public void ATargetIsReadIntoAHostAndAPort(string text, string host, int port)
    {
        Assert.True(ProxyRules.Target(text, out var read, out var number));
        Assert.Equal(host, read);
        Assert.Equal(port, number);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:")]
    [InlineData(":51820")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:70000")]
    [InlineData("../secret:53")]
    public void ATargetTheRelayCannotReachIsRefused(string text)
    {
        Assert.False(ProxyRules.Target(text, out _, out _));
    }

    [Fact]
    public void AWireguardProxyIsTurnedOnWithoutACertificate()
    {
        var relay = ProxyDefaults.Fresh("proxy0", ProxyKind.Wg) with
        {
            IsEnabled = true,
            Target = "127.0.0.1:51820",
        };

        Assert.Null(ProxyRules.CheckReady(relay, string.Empty, string.Empty));
        Assert.Equal("no-certificate", ProxyRules.CheckReady(Fresh() with { IsEnabled = true }, string.Empty, string.Empty)?.Code);
    }

    [Fact]
    public void TheArgumentsOfAWireguardProxyNameTheListenerAndTheTarget()
    {
        var relay = ProxyDefaults.Fresh("proxy0", ProxyKind.Wg) with { Port = 443, Target = "10.0.0.2:51820" };
        var line = ProxyFile.Line(relay, string.Empty, string.Empty, "/etc/proxy-proxy0.yaml");

        Assert.StartsWith("PROXY_ARGS=--listen 0.0.0.0:443", line, StringComparison.Ordinal);
        Assert.Contains("--target 10.0.0.2:51820", line, StringComparison.Ordinal);
        Assert.DoesNotContain("--restrict-config", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWireguardProxyRunsItsOwnServiceAndWritesNoWhitelist()
    {
        var tools = new Tools();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, new Ledger(), directory);

        try
        {
            var state = await host.ApplyAsync(
                ProxyDefaults.Fresh("proxy0", ProxyKind.Wg) with { IsEnabled = true, Target = "127.0.0.1:51820" },
                [51820],
                string.Empty,
                string.Empty,
                CancellationToken.None);

            Assert.True(state.IsRunning);
            Assert.True(tools.Called("systemctl restart amneziageo-relay@proxy0"));
            Assert.False(File.Exists(host.RulesPath("proxy0")));
            Assert.Contains(
                "--target 127.0.0.1:51820",
                await File.ReadAllTextAsync(host.ArgumentsPath("proxy0")),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.1.2.3/8")]
    [InlineData("one.two")]
    public void ASourceThatIsNeitherAnAddressNorANetworkIsRefused(string source)
    {
        Assert.Equal("bad-source", ProxyRules.Check(Fresh() with { Sources = [source] })?.Code);
    }

    [Fact]
    public void ASourceIsTakenAsAnAddressAndAsANetwork()
    {
        Assert.Null(ProxyRules.Check(Fresh() with { Sources = ["10.1.0.0/16", "203.0.113.7", "2001:db8::/32"] }));
    }

    [Fact]
    public void TheRulesetDropsWhatReachesAProxyFromASourceItDoesNotName()
    {
        var proxy = Fresh() with { IsEnabled = true, Sources = ["10.1.0.0/16", "203.0.113.7"] };

        var text = ProxyRuleset.Text([proxy]);

        Assert.Contains(
            "tcp dport 443 ip saddr != { 10.1.0.0/16, 203.0.113.7/32 } drop",
            text,
            StringComparison.Ordinal);
        Assert.Contains("tcp dport 443 meta nfproto ipv6 drop", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRulesetHoldsARelayByTheDatagramsItTakes()
    {
        var proxy = ProxyDefaults.Fresh("relay0", ProxyKind.Wg) with
        {
            IsEnabled = true,
            Port = 51821,
            Target = "127.0.0.1:51820",
            Sources = ["2001:db8::/32"],
        };

        var text = ProxyRuleset.Text([proxy]);

        Assert.Contains("udp dport 51821 meta nfproto ipv4 drop", text, StringComparison.Ordinal);
        Assert.Contains("udp dport 51821 ip6 saddr != { 2001:db8::/32 } drop", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AProxyThatNamesNoSourceAndOneThatIsOffAreLeftOpen()
    {
        var open = Fresh() with { IsEnabled = true };
        var off = ProxyDefaults.Fresh("proxy1") with { Port = 8443, Sources = ["10.1.0.0/16"] };

        var text = ProxyRuleset.Text([open, off]);

        Assert.Contains("table inet amneziageo_proxy", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dport", text, StringComparison.Ordinal);
    }

    private static ProxyConfig Fresh() => ProxyDefaults.Fresh(ProxyDefaults.FirstName);

    private static int Count(string text, string part)
    {
        var found = 0;
        var at = text.IndexOf(part, StringComparison.Ordinal);
        while (at >= 0)
        {
            found++;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
        }

        return found;
    }
}
