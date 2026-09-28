using System.Globalization;
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

    private const string Properties = "--property=ActiveState,SubState,NRestarts,ExecMainStatus";

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
    /// Returns whether the service of a front is up and how many times systemd started it again.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, CancellationToken ct)
    {
        var shown = await _commands.RunAsync(Tool, ["show", ProxyHost.Unit(name), Properties], null, ct)
            .ConfigureAwait(false);

        return shown.IsOk ? StateOf(shown.Output) : new ProxyState(false, shown.Complaint);
    }

    /// <summary>
    /// Reads the state of a front out of what systemctl shows of its service.
    /// </summary>
    public static ProxyState StateOf(string shown)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (shown ?? string.Empty).Split('\n'))
        {
            var at = line.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
            {
                values[line[..at].Trim()] = line[(at + 1)..].Trim();
            }
        }

        var active = values.GetValueOrDefault("ActiveState", string.Empty);
        var state = values.GetValueOrDefault("SubState", string.Empty);
        var code = values.GetValueOrDefault("ExecMainStatus", string.Empty);
        var falls = long.TryParse(values.GetValueOrDefault("NRestarts"), NumberStyles.None, CultureInfo.InvariantCulture, out var restarts)
            ? restarts
            : 0;
        var fell = code is "" or "0" ? string.Empty : $"{ChildProxies.Tunnel} ended with code {code}";
        if (active is "active" or "reloading")
        {
            return new ProxyState(true, fell, falls);
        }

        var said = state.Length > 0 && state != active ? $"{active} ({state})" : active;

        return new ProxyState(false, fell.Length > 0 ? $"{said}: {fell}" : said, falls);
    }

    private async Task<ProxyState> RunAsync(string[] arguments, ProxyState done, CancellationToken ct)
    {
        var result = await _commands.RunAsync(Tool, arguments, null, ct).ConfigureAwait(false);

        return result.IsOk ? done : new ProxyState(false, result.Complaint);
    }
}
