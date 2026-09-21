using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Runs the services of the websocket fronts.
/// </summary>
public interface IProxyRunner
{
    /// <summary>
    /// Makes the service of a front start with the host.
    /// </summary>
    Task<ProxyState> EnableAsync(string name, CancellationToken ct);

    /// <summary>
    /// Starts the service of a front over.
    /// </summary>
    Task<ProxyState> StartAsync(string name, CancellationToken ct);

    /// <summary>
    /// Takes the service of a front down and keeps it from starting with the host.
    /// </summary>
    Task<ProxyState> StopAsync(string name, CancellationToken ct);

    /// <summary>
    /// Returns whether the service of a front is up.
    /// </summary>
    Task<ProxyState> StateAsync(string name, CancellationToken ct);
}

/// <summary>
/// Runs the fronts as services of systemd.
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
    /// Makes the service of a front start with the host.
    /// </summary>
    public Task<ProxyState> EnableAsync(string name, CancellationToken ct) =>
        RunAsync(["enable", ProxyHost.Unit(name)], ProxyState.Down, ct);

    /// <summary>
    /// Starts the service of a front over.
    /// </summary>
    public Task<ProxyState> StartAsync(string name, CancellationToken ct) =>
        RunAsync(["restart", ProxyHost.Unit(name)], ProxyState.Up, ct);

    /// <summary>
    /// Takes the service of a front down, with the relay of the same name a release before ran, and keeps both from
    /// starting with the host.
    /// </summary>
    public async Task<ProxyState> StopAsync(string name, CancellationToken ct)
    {
        await _commands.RunAsync(Tool, ["disable", "--now", $"{ProxyDefaults.RelayService}@{name}"], null, ct).ConfigureAwait(false);

        return await RunAsync(["disable", "--now", ProxyHost.Unit(name)], ProxyState.Down, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns whether the service of a front is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, CancellationToken ct)
    {
        var active = await _commands.RunAsync(Tool, ["is-active", ProxyHost.Unit(name)], null, ct)
            .ConfigureAwait(false);

        return active.IsOk ? ProxyState.Up : new ProxyState(false, active.Complaint);
    }

    private async Task<ProxyState> RunAsync(string[] arguments, ProxyState done, CancellationToken ct)
    {
        var result = await _commands.RunAsync(Tool, arguments, null, ct).ConfigureAwait(false);

        return result.IsOk ? done : new ProxyState(false, result.Complaint);
    }
}
