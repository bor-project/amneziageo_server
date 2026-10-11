using System.Diagnostics;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Tests;

/// <summary>
/// The service is handed a socket for its notifications to systemd, and a process it starts must not inherit it: a
/// tool of systemd that finds the socket reports its own end there, and systemd writes a refusal into the journal of
/// the service for each.
/// </summary>
public sealed class ServiceSocketTests : IDisposable
{
    private const string Socket = "/run/amneziageo-test/notify";

    private readonly string? _before = Environment.GetEnvironmentVariable(ServiceSocket.Variable);

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-socket-");

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceSocketTests()
    {
        Environment.SetEnvironmentVariable(ServiceSocket.Variable, Socket);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ServiceSocket.Variable, _before);
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void TheSocketIsTakenOutOfWhatAProcessIsStartedWith()
    {
        var start = new ProcessStartInfo("env") { UseShellExecute = false };

        Assert.Equal(Socket, start.Environment[ServiceSocket.Variable]);
        Assert.Same(start, ServiceSocket.Withhold(start));
        Assert.False(start.Environment.ContainsKey(ServiceSocket.Variable));
        Assert.True(start.Environment.ContainsKey("PATH"));
    }

    [Fact]
    public async Task AToolOfTheHostDoesNotSeeTheSocket()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var ran = await new HostCommands().RunAsync("env", [], null, CancellationToken.None);

        Assert.True(ran.IsOk);
        Assert.Contains("PATH=", ran.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ServiceSocket.Variable + "=", ran.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFrontRunAsAChildDoesNotSeeTheSocket()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var tool = Path.Combine(_folder.FullName, "tool");
        var seen = Path.Combine(_folder.FullName, "seen");
        File.WriteAllText(tool, "#!/bin/sh\nenv > \"$0.part\"\nmv \"$0.part\" \"$(dirname \"$0\")/seen\"\nexec sleep 30\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(_folder.FullName, ProxyFile.Arguments("awg0")), "PROXY_ARGS=ws://127.0.0.1:61001\n");
        using var proxies = new ChildProxies(_folder.FullName, tool, TimeSpan.FromMilliseconds(50));

        await proxies.StartAsync("awg0", CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(seen) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var environment = await File.ReadAllTextAsync(seen);
        await proxies.StopAsync("awg0", CancellationToken.None);

        Assert.Contains("PATH=", environment, StringComparison.Ordinal);
        Assert.DoesNotContain(ServiceSocket.Variable + "=", environment, StringComparison.Ordinal);
    }
}
