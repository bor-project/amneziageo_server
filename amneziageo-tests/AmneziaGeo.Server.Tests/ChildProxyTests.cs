using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Tests;

public class ChildProxyTests
{
    [Fact]
    public void AWireguardProxyRunsTheConsoleAndAWebsocketProxyTheTunnel()
    {
        var relay = ChildProxies.Command(ProxyKind.Wg, "/opt/console", "wstunnel", ["--listen", "0.0.0.0:443"]);
        var tunnel = ChildProxies.Command(ProxyKind.Ws, "/opt/console", "wstunnel", ["ws://0.0.0.0:443"]);

        Assert.Equal("/opt/console", relay.File);
        Assert.Equal(["relay", "--listen", "0.0.0.0:443"], relay.Arguments);
        Assert.Equal("wstunnel", tunnel.File);
        Assert.Equal(["server", "ws://0.0.0.0:443"], tunnel.Arguments);
    }

    [Fact]
    public void TheArgumentsAreReadOutOfTheLineTheServiceTakes()
    {
        var relay = ProxyDefaults.Fresh("proxy0", ProxyKind.Wg) with { Port = 443, Target = "10.0.0.2:51820" };
        var line = ProxyFile.Line(relay, string.Empty, string.Empty, "/etc/proxy-proxy0.yaml");

        Assert.Equal(["--listen", "0.0.0.0:443", "--target", "10.0.0.2:51820", "--idle", "180"], ChildProxies.Arguments(line));
        Assert.Empty(ChildProxies.Arguments("# nothing to run\n"));
    }

    [Fact]
    public async Task AProxyRunsUntilItIsTakenDown()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Tool("#!/bin/sh\nexec sleep 30\n");
        place.Arguments("proxy0", "PROXY_ARGS=ws://0.0.0.0:443\n");
        using var proxies = place.Proxies();

        var started = await proxies.StartAsync("proxy0", ProxyKind.Ws, CancellationToken.None);
        var running = await proxies.StateAsync("proxy0", ProxyKind.Ws, CancellationToken.None);
        await proxies.StopAsync("proxy0", ProxyKind.Ws, CancellationToken.None);
        var stopped = await proxies.StateAsync("proxy0", ProxyKind.Ws, CancellationToken.None);

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
        place.Arguments("proxy0", "PROXY_ARGS=--listen 0.0.0.0:443 --target 127.0.0.1:51820\n");
        using var proxies = place.Proxies();

        await proxies.StartAsync("proxy0", ProxyKind.Wg, CancellationToken.None);
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

        var state = await proxies.StartAsync("proxy0", ProxyKind.Ws, CancellationToken.None);

        Assert.False(state.IsRunning);
        Assert.Contains("proxy-proxy0.env", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AToolTheHostLacksIsNamed()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Arguments("proxy0", "PROXY_ARGS=ws://0.0.0.0:443\n");
        var missing = Path.Combine(place.Root, "missing");
        using var proxies = new ChildProxies(place.Root, missing, missing);

        var state = await proxies.StartAsync("proxy0", ProxyKind.Ws, CancellationToken.None);

        Assert.False(state.IsRunning);
        Assert.Contains(missing, state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHostOfTheProxiesRunsThemThroughTheRunnerItIsGiven()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        using var place = new Place();
        place.Tool("#!/bin/sh\nexec sleep 30\n");
        using var proxies = place.Proxies();
        var tools = new Tools();
        var host = new ProxyHost(tools, new Ledger(), place.Root, proxies);
        var proxy = ProxyDefaults.Fresh(ProxyDefaults.FirstName) with { IsEnabled = true, Path = "v1" };

        var state = await host.ApplyAsync(proxy, [51820], "/tls/chain.pem", "/tls/key.pem", CancellationToken.None);
        var again = await host.ApplyAsync(proxy, [51820], "/tls/chain.pem", "/tls/key.pem", CancellationToken.None);
        var off = await host.ApplyAsync(proxy with { IsEnabled = false }, [51820], "/tls/chain.pem", "/tls/key.pem", CancellationToken.None);

        Assert.True(state.IsRunning);
        Assert.True(again.IsRunning);
        Assert.False(off.IsRunning);
        Assert.False((await proxies.StateAsync(proxy.Name, proxy.Kind, CancellationToken.None)).IsRunning);
        Assert.DoesNotContain(tools.Calls, call => call.StartsWith("systemctl", StringComparison.Ordinal));
    }

    private static int Lines(string path) => File.Exists(path) ? File.ReadAllLines(path).Length : 0;

    // A directory of its own for the files and the tool of a proxy.
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

        public ChildProxies Proxies() => new(Root, ToolPath, ToolPath, TimeSpan.FromMilliseconds(50));

        public void Dispose()
        {
            Directory.Delete(Root, true);
        }
    }
}
