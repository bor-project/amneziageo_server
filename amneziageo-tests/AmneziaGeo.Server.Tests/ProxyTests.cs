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
        var host = new ProxyHost(new Tools(), "/etc/amneziageo-server");

        Assert.Equal("/etc/amneziageo-server/proxy-a.yaml", host.RulesPath("a"));
        Assert.Equal("/etc/amneziageo-server/proxy-b.env", host.ArgumentsPath("b"));
        Assert.Equal("amneziageo-proxy@a", ProxyHost.Unit("a"));
    }

    [Fact]
    public async Task TurningAProxyOnWritesItsFilesAndStartsItsService()
    {
        var tools = new Tools();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var host = new ProxyHost(tools, directory);

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
        var host = new ProxyHost(tools, directory);

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
        var host = new ProxyHost(tools, directory);

        try
        {
            await host.ApplyAsync(
                Fresh() with { IsEnabled = true },
                [51820],
                "/tls/chain.pem",
                "/tls/key.pem",
                CancellationToken.None);

            await host.WithdrawAsync("proxy0", CancellationToken.None);

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
        var host = new ProxyHost(tools, directory);

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
