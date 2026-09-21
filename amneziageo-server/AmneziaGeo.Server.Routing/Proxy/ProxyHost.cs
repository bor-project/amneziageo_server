using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Holds the websocket fronts of the endpoints: writes what each may reach and runs its service.
/// </summary>
public sealed class ProxyHost
{
    /// <summary>
    /// The firewall table the rules of the proxies of the releases before lived in.
    /// </summary>
    public const string SourcesTable = "amneziageo_proxy";

    private readonly IProxyRunner _runner;

    private readonly IHostNetwork _network;

    private readonly string _directory;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyHost(IHostNetwork network, IProxyRunner runner, string? directory = null)
    {
        _network = network;
        _runner = runner;
        _directory = string.IsNullOrWhiteSpace(directory) ? ProxyDefaults.Directory : directory;
    }

    /// <summary>
    /// Returns the service the front of an interface runs as.
    /// </summary>
    public static string Unit(string name) => $"{ProxyDefaults.Service}@{name}";

    /// <summary>
    /// Returns the file the allowed target of a front is written to.
    /// </summary>
    public string RulesPath(string name) => Path.Combine(_directory, ProxyFile.Rules(name));

    /// <summary>
    /// Returns the file the arguments of a front are written to.
    /// </summary>
    public string ArgumentsPath(string name) => Path.Combine(_directory, ProxyFile.Arguments(name));

    /// <summary>
    /// Lists the names the directory holds arguments for.
    /// </summary>
    public IReadOnlyList<string> Held()
    {
        try
        {
            return Directory.Exists(_directory)
                ? [.. Directory.EnumerateFiles(_directory).Select(ProxyFile.Name).OfType<string>().Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Puts the front of an interface on the host, starting its service over only when its files change or it is down.
    /// </summary>
    public async Task<ProxyState> ApplyAsync(string name, int front, int target, CancellationToken ct)
    {
        try
        {
            var written = await WriteAsync(name, front, target, ct).ConfigureAwait(false);
            if (written.Fault.Length > 0)
            {
                return new ProxyState(false, written.Fault);
            }

            var enabled = await _runner.EnableAsync(name, ct).ConfigureAwait(false);
            if (enabled.Message.Length > 0)
            {
                return enabled;
            }

            if (!written.IsChanged)
            {
                var running = await StateAsync(name, ct).ConfigureAwait(false);
                if (running.IsRunning)
                {
                    return running;
                }
            }

            var started = await _runner.StartAsync(name, ct).ConfigureAwait(false);

            return started.Message.Length > 0
                ? started
                : await StateAsync(name, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Takes the service of a front down and clears its files.
    /// </summary>
    public async Task<ProxyState> WithdrawAsync(string name, CancellationToken ct)
    {
        try
        {
            var stopped = await _runner.StopAsync(name, ct).ConfigureAwait(false);
            Clear(name);

            return stopped;
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Drops the rules the proxies of the releases before held their sources with.
    /// </summary>
    public async Task ForgetSourcesAsync(CancellationToken ct)
    {
        try
        {
            await _network.FirewallAsync($"table inet {SourcesTable}\ndelete table inet {SourcesTable}\n", ct).ConfigureAwait(false);
        }
        catch (HostNetworkException)
        {
        }
    }

    /// <summary>
    /// Returns whether the service of a front is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, CancellationToken ct)
    {
        try
        {
            return await _runner.StateAsync(name, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    private async Task<Written> WriteAsync(string name, int front, int target, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var rules = RulesPath(name);
            var whitelist = await PutAsync(rules, ProxyFile.Whitelist(target), ct).ConfigureAwait(false);
            var arguments = await PutAsync(ArgumentsPath(name), ProxyFile.Line(front, rules), ct).ConfigureAwait(false);

            return new Written(whitelist || arguments, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Written(false, ex.Message);
        }
    }

    private static async Task<bool> PutAsync(string path, string text, CancellationToken ct)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) == text)
        {
            return false;
        }

        await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);

        return true;
    }

    private void Clear(string name)
    {
        try
        {
            File.Delete(RulesPath(name));
            File.Delete(ArgumentsPath(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record Written(bool IsChanged, string Fault);
}
