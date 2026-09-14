using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Holds the proxies of the host: writes what each may reach and runs its service.
/// </summary>
public sealed class ProxyHost
{
    private readonly IProxyRunner _runner;

    private readonly IHostNetwork _network;

    private readonly string _directory;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyHost(IHostCommands commands, IHostNetwork network, string? directory = null, IProxyRunner? runner = null)
    {
        _runner = runner ?? new SystemdProxies(commands);
        _network = network;
        _directory = string.IsNullOrWhiteSpace(directory) ? ProxyDefaults.Directory : directory;
    }

    /// <summary>
    /// Returns the service a proxy of a kind runs as.
    /// </summary>
    public static string Unit(string name, string? kind = null)
    {
        var service = ProxyKind.HasTarget(kind) ? ProxyDefaults.RelayService : ProxyDefaults.Service;

        return $"{service}@{name}";
    }

    /// <summary>
    /// Returns the file the allowed targets of a proxy are written to.
    /// </summary>
    public string RulesPath(string name) => Path.Combine(_directory, ProxyFile.Rules(name));

    /// <summary>
    /// Returns the file the arguments of a proxy are written to.
    /// </summary>
    public string ArgumentsPath(string name) => Path.Combine(_directory, ProxyFile.Arguments(name));

    /// <summary>
    /// Puts a proxy on the host, starting its service over only when its files change or it is down.
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
                return await _runner.StopAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);
            }

            var written = await WriteAsync(proxy, ports, certificate, key, ct).ConfigureAwait(false);
            if (written.Fault.Length > 0)
            {
                return new ProxyState(false, written.Fault);
            }

            var enabled = await _runner.EnableAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);
            if (enabled.Message.Length > 0)
            {
                return enabled;
            }

            if (!written.IsChanged)
            {
                var running = await StateAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);
                if (running.IsRunning)
                {
                    return running;
                }
            }

            var started = await _runner.StartAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);

            return started.Message.Length > 0
                ? started
                : await StateAsync(proxy.Name, proxy.Kind, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Takes the service of a proxy down and clears its files.
    /// </summary>
    public async Task<ProxyState> WithdrawAsync(string name, string kind, CancellationToken ct)
    {
        try
        {
            var stopped = await _runner.StopAsync(name, kind, ct).ConfigureAwait(false);
            Clear(name);

            return stopped;
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    /// <summary>
    /// Lays the rules that hold every proxy to the sources it names.
    /// </summary>
    public async Task<string> FirewallAsync(IReadOnlyList<ProxyConfig> proxies, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxies);

        try
        {
            await _network.FirewallAsync(ProxyRuleset.Text(proxies), ct).ConfigureAwait(false);

            return string.Empty;
        }
        catch (HostNetworkException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    public async Task<ProxyState> StateAsync(string name, string kind, CancellationToken ct)
    {
        try
        {
            return await _runner.StateAsync(name, kind, ct).ConfigureAwait(false);
        }
        catch (HostNetworkException ex)
        {
            return new ProxyState(false, ex.Message);
        }
    }

    private async Task<Written> WriteAsync(
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
            var whitelist = ProxyKind.HasPath(proxy.Kind)
                && await PutAsync(rules, ProxyFile.Whitelist(proxy.Path, ports), ct).ConfigureAwait(false);
            var arguments = await PutAsync(ArgumentsPath(proxy.Name), ProxyFile.Line(proxy, certificate, key, rules), ct)
                .ConfigureAwait(false);

            return new Written(whitelist || arguments, string.Empty);
        }
        catch (IOException ex)
        {
            return new Written(false, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Written(bool IsChanged, string Fault);
}
