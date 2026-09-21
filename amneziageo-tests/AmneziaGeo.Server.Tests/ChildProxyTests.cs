using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Tests;

public class ChildProxyTests
{
    [Fact]
    public void AFrontRunsTheTunnelAsAServer()
    {
        var tunnel = ChildProxies.Command("wstunnel", ["ws://127.0.0.1:61001"]);

        Assert.Equal("wstunnel", tunnel.File);
        Assert.Equal(["server", "ws://127.0.0.1:61001"], tunnel.Arguments);
    }

    [Fact]
    public void TheArgumentsAreReadOutOfTheLineTheServiceTakes()
    {
        var line = ProxyFile.Line(61001, "/etc/proxy-awg0.yaml");

        Assert.Equal(["ws://127.0.0.1:61001", "--restrict-config", "/etc/proxy-awg0.yaml"], ChildProxies.Arguments(line));
        Assert.Empty(ChildProxies.Arguments("# nothing to run\n"));
    }

    [Fact]
    public void TheNameOfAFrontIsReadBackFromItsFile()
    {
        Assert.Equal("awg0", ProxyFile.Name("/etc/amneziageo-server/" + ProxyFile.Arguments("awg0")));
        Assert.Null(ProxyFile.Name("/etc/amneziageo-server/proxy-.env"));
        Assert.Null(ProxyFile.Name("/etc/amneziageo-server/server.env"));
        Assert.Null(ProxyFile.Name("/etc/amneziageo-server/" + ProxyFile.Rules("awg0")));
    }

    [Fact]
    public void TheWhitelistLetsTheTunnelReachThePortOfItsInterfaceAlone()
    {
        var text = ProxyFile.Whitelist(51820);

        Assert.Contains("!PathPrefix '^v1$'", text, StringComparison.Ordinal);
        Assert.Contains("- Udp", text, StringComparison.Ordinal);
        Assert.Contains("- 51820\n", text, StringComparison.Ordinal);
        Assert.Contains("host: '^127\\.0\\.0\\.1$'", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProxyRunsUntilItIsTakenDown()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Tool("#!/bin/sh\nexec sleep 30\n");
        place.Arguments("awg0", "PROXY_ARGS=ws://127.0.0.1:61001\n");
        using var proxies = place.Proxies();

        var started = await proxies.StartAsync("awg0", CancellationToken.None);
        var running = await proxies.StateAsync("awg0", CancellationToken.None);
        await proxies.StopAsync("awg0", CancellationToken.None);
        var stopped = await proxies.StateAsync("awg0", CancellationToken.None);

        Assert.True(started.IsRunning);
        Assert.True(running.IsRunning);
        Assert.False(stopped.IsRunning);
    }

    [Fact]
    public async Task AProxyThatFallsOverIsStartedAgain()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Tool("""
            #!/bin/sh
            echo run >> "$(dirname "$0")/runs"
            exit 3
            """);
        place.Arguments("awg0", "PROXY_ARGS=ws://127.0.0.1:61001\n");
        using var proxies = place.Proxies();

        await proxies.StartAsync("awg0", CancellationToken.None);
        var runs = Path.Combine(place.Root, "runs");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Lines(runs) < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(Lines(runs) >= 3);
    }

    [Fact]
    public async Task AProxyWithoutItsFileSaysWhy()
    {
        using var place = new Place();
        using var proxies = place.Proxies();

        var state = await proxies.StartAsync("awg0", CancellationToken.None);

        Assert.False(state.IsRunning);
        Assert.Contains("proxy-awg0.env", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AToolTheHostLacksIsNamed()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Arguments("awg0", "PROXY_ARGS=ws://127.0.0.1:61001\n");
        var missing = Path.Combine(place.Root, "missing");
        using var proxies = new ChildProxies(place.Root, missing);

        var state = await proxies.StartAsync("awg0", CancellationToken.None);

        Assert.False(state.IsRunning);
        Assert.Contains(missing, state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHostOfTheFrontsRunsThemThroughTheRunnerItIsGiven()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Tool("#!/bin/sh\nexec sleep 30\n");
        using var proxies = place.Proxies();
        var host = new ProxyHost(new Ledger(), proxies, place.Root);

        var state = await host.ApplyAsync("awg0", 61001, 51820, CancellationToken.None);
        var again = await host.ApplyAsync("awg0", 61001, 51820, CancellationToken.None);
        var held = host.Held();
        var line = await File.ReadAllTextAsync(host.ArgumentsPath("awg0"));
        var gone = await host.WithdrawAsync("awg0", CancellationToken.None);

        Assert.True(state.IsRunning);
        Assert.True(again.IsRunning);
        Assert.Equal(["awg0"], held);
        Assert.Equal(ProxyFile.Line(61001, host.RulesPath("awg0")), line);
        Assert.False(gone.IsRunning);
        Assert.False((await proxies.StateAsync("awg0", CancellationToken.None)).IsRunning);
        Assert.Empty(host.Held());
    }

    private static int Lines(string path) => File.Exists(path) ? File.ReadAllLines(path).Length : 0;

    // A directory of its own for the files and the tool of a front.
    private sealed class Place : IDisposable
    {
        public Place()
        {
            Root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        private string ToolPath => Path.Combine(Root, "tool");

        public void Tool(string script)
        {
            File.WriteAllText(ToolPath, script);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ToolPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public void Arguments(string name, string text)
        {
            File.WriteAllText(Path.Combine(Root, ProxyFile.Arguments(name)), text);
        }

        public ChildProxies Proxies() => new(Root, ToolPath, TimeSpan.FromMilliseconds(50));

        public void Dispose()
        {
            Directory.Delete(Root, true);
        }
    }
}
