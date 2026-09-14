using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Runs the services of the proxies.
/// </summary>
public interface IProxyRunner
{
    /// <summary>
    /// Makes the service of a proxy start with the host.
    /// </summary>
    Task<ProxyState> EnableAsync(string name, string kind, CancellationToken ct);

    /// <summary>
    /// Starts the service of a proxy over.
    /// </summary>
    Task<ProxyState> StartAsync(string name, string kind, CancellationToken ct);

    /// <summary>
    /// Takes the service of a proxy down and keeps it from starting with the host.
    /// </summary>
    Task<ProxyState> StopAsync(string name, string kind, CancellationToken ct);

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    Task<ProxyState> StateAsync(string name, string kind, CancellationToken ct);
}

/// <summary>
/// Runs the proxies as services of systemd.
/// </summary>
public sealed class SystemdProxies : IProxyRunner
{
    private const string Tool = "systemctl";

    private const string Marker = "/run/systemd/system";

    private readonly IHostCommands _commands;

    /// <summary>
    /// ctor
    /// </summary>
    public SystemdProxies(IHostCommands commands)
    {
        _commands = commands;
    }

    /// <summary>
    /// Tells whether the host runs systemd.
    /// </summary>
    public static bool Runs => Directory.Exists(Marker);

    /// <summary>
    /// Makes the service of a proxy start with the host.
    /// </summary>
    public Task<ProxyState> EnableAsync(string name, string kind, CancellationToken ct) =>
        RunAsync(["enable", ProxyHost.Unit(name, kind)], ProxyState.Down, ct);

    /// <summary>
    /// Starts the service of a proxy over.
    /// </summary>
    public Task<ProxyState> StartAsync(string name, string kind, CancellationToken ct) =>
        RunAsync(["restart", ProxyHost.Unit(name, kind)], ProxyState.Up, ct);

    /// <summary>
    /// Takes the service of a proxy down and keeps it from starting with the host.
    /// </summary>
    public Task<ProxyState> StopAsync(string name, string kind, CancellationToken ct) =>
        RunAsync(["disable", "--now", ProxyHost.Unit(name, kind)], ProxyState.Down, ct);

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, string kind, CancellationToken ct)
    {
        var active = await _commands.RunAsync(Tool, ["is-active", ProxyHost.Unit(name, kind)], null, ct)
            .ConfigureAwait(false);

        return active.IsOk ? ProxyState.Up : new ProxyState(false, active.Complaint);
    }

    private async Task<ProxyState> RunAsync(string[] arguments, ProxyState done, CancellationToken ct)
    {
        var result = await _commands.RunAsync(Tool, arguments, null, ct).ConfigureAwait(false);

        return result.IsOk ? done : new ProxyState(false, result.Complaint);
    }
}
