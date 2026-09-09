using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Holds the websocket proxies of the host: writes what each may reach and runs its service.
/// </summary>
public sealed class ProxyHost
{
    private const string Tool = "systemctl";

    private readonly IHostCommands _commands;

    private readonly string _directory;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyHost(IHostCommands commands, string? directory = null)
    {
        _commands = commands;
        _directory = string.IsNullOrWhiteSpace(directory) ? ProxyDefaults.Directory : directory;
    }

    /// <summary>
    /// Returns the service a proxy runs as.
    /// </summary>
    public static string Unit(string name) => $"{ProxyDefaults.Service}@{name}";

    /// <summary>
    /// Returns the file the allowed targets of a proxy are written to.
    /// </summary>
    public string RulesPath(string name) => Path.Combine(_directory, ProxyFile.Rules(name));

    /// <summary>
    /// Returns the file the arguments of a proxy are written to.
    /// </summary>
    public string ArgumentsPath(string name) => Path.Combine(_directory, ProxyFile.Arguments(name));

    /// <summary>
    /// Puts a proxy on the host, and takes its service down when the proxy is turned off.
    /// </summary>
    public async Task<ProxyState> ApplyAsync(
        ProxyConfig proxy,
        IReadOnlyList<int> ports,
        string certificate,
        string key,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(ports);

        try
        {
            if (!proxy.IsEnabled)
            {
                return await StopAsync(proxy.Name, ct).ConfigureAwait(false);
            }

            var written = await WriteAsync(proxy, ports, certificate, key, ct).ConfigureAwait(false);
            if (written.Length > 0)
            {
                return new ProxyState(false, written);
            }

            var enabled = await RunAsync(["enable", Unit(proxy.Name)], ProxyState.Down, ct).ConfigureAwait(false);
            if (enabled.Message.Length > 0)
            {
                return enabled;
            }

            var started = await RunAsync(["restart", Unit(proxy.Name)], ProxyState.Up, ct).ConfigureAwait(false);

            return started.Message.Length > 0
                ? started
                : await StateAsync(proxy.Name, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Takes the service of a proxy down and clears its files.
    /// </summary>
    public async Task<ProxyState> WithdrawAsync(string name, CancellationToken ct)
    {
        try
        {
            var stopped = await StopAsync(name, ct).ConfigureAwait(false);
            Clear(name);

            return stopped;
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, CancellationToken ct)
    {
        try
        {
            var active = await _commands.RunAsync(Tool, ["is-active", Unit(name)], null, ct).ConfigureAwait(false);

            return active.IsOk ? ProxyState.Up : new ProxyState(false, active.Complaint);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    private async Task<ProxyState> StopAsync(string name, CancellationToken ct) =>
        await RunAsync(["disable", "--now", Unit(name)], ProxyState.Down, ct).ConfigureAwait(false);

    private async Task<ProxyState> RunAsync(string[] arguments, ProxyState done, CancellationToken ct)
    {
        var result = await _commands.RunAsync(Tool, arguments, null, ct).ConfigureAwait(false);

        return result.IsOk ? done : new ProxyState(false, result.Complaint);
    }

    private async Task<string> WriteAsync(
        ProxyConfig proxy,
        IReadOnlyList<int> ports,
        string certificate,
        string key,
        CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var rules = RulesPath(proxy.Name);
            await File.WriteAllTextAsync(rules, ProxyFile.Whitelist(proxy.Path, ports), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    ArgumentsPath(proxy.Name),
                    ProxyFile.Line(proxy, certificate, key, rules),
                    ct)
                .ConfigureAwait(false);

            return string.Empty;
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex.Message;
        }
    }

    private void Clear(string name)
    {
        try
        {
            File.Delete(RulesPath(name));
            File.Delete(ArgumentsPath(name));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
