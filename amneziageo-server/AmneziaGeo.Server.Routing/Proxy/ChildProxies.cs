using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// The tool a proxy runs as and what it is given.
/// </summary>
/// <param name="File">The tool.</param>
/// <param name="Arguments">What the tool is given.</param>
public sealed record ProxyCommand(string File, IReadOnlyList<string> Arguments);

/// <summary>
/// Runs the websocket fronts as processes of the server, on a host that carries no systemd.
/// </summary>
public sealed class ChildProxies : IProxyRunner, IDisposable
{
    /// <summary>
    /// The tool a websocket front runs as.
    /// </summary>
    public const string Tunnel = "wstunnel";

    /// <summary>
    /// How long a proxy that fell over waits before it starts again.
    /// </summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

    private readonly string _directory;

    private readonly string _tunnel;

    private readonly TimeSpan _pause;

    private readonly ConcurrentDictionary<string, Child> _children = new(StringComparer.Ordinal);

    /// <summary>
    /// ctor
    /// </summary>
    public ChildProxies(string? directory = null, string? tunnel = null, TimeSpan? pause = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? ProxyDefaults.Directory : directory;
        _tunnel = string.IsNullOrWhiteSpace(tunnel) ? Tunnel : tunnel;
        _pause = pause ?? Pause;
    }

    /// <summary>
    /// Returns the command a front runs with the arguments of its file.
    /// </summary>
    public static ProxyCommand Command(string tunnel, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new ProxyCommand(tunnel, ["server", .. arguments]);
    }

    /// <summary>
    /// Returns the arguments the file of a proxy carries.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string text)
    {
        var prefix = ProxyFile.Variable + "=";
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        return [];
    }

    /// <summary>
    /// Makes the service of a proxy start with the host.
    /// </summary>
    public Task<ProxyState> EnableAsync(string name, CancellationToken ct) =>
        Task.FromResult(ProxyState.Down);

    /// <summary>
    /// Starts the service of a proxy over.
    /// </summary>
    public async Task<ProxyState> StartAsync(string name, CancellationToken ct)
    {
        Drop(name);

        var file = ProxyFile.Arguments(name);
        var read = await ReadAsync(Path.Combine(_directory, file), ct).ConfigureAwait(false);
        if (read.Fault.Length > 0)
        {
            return new ProxyState(false, read.Fault);
        }

        var arguments = Arguments(read.Text);
        if (arguments.Count == 0)
        {
            return new ProxyState(false, $"{file} names no {ProxyFile.Variable}");
        }

        var child = new Child(Command(_tunnel, arguments), _pause);
        var state = child.Start();
        if (state.IsRunning)
        {
            _children[ProxyHost.Unit(name)] = child;
        }
        else
        {
            child.Dispose();
        }

        return state;
    }

    /// <summary>
    /// Takes the service of a proxy down and keeps it from starting with the host.
    /// </summary>
    public Task<ProxyState> StopAsync(string name, CancellationToken ct)
    {
        Drop(name);

        return Task.FromResult(ProxyState.Down);
    }

    /// <summary>
    /// Returns whether the service of a proxy is up.
    /// </summary>
    public Task<ProxyState> StateAsync(string name, CancellationToken ct) =>
        Task.FromResult(_children.TryGetValue(ProxyHost.Unit(name), out var child) ? child.State : ProxyState.Down);

    /// <summary>
    /// Takes every proxy down.
    /// </summary>
    public void Dispose()
    {
        foreach (var key in _children.Keys)
        {
            if (_children.TryRemove(key, out var child))
            {
                child.Dispose();
            }
        }
    }

    private static async Task<(string Text, string Fault)> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return (await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), string.Empty);
        }
        catch (IOException ex)
        {
            return (string.Empty, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (string.Empty, ex.Message);
        }
    }

    private void Drop(string name)
    {
        if (_children.TryRemove(ProxyHost.Unit(name), out var child))
        {
            child.Dispose();
        }
    }

    // A process of a proxy, started again whenever it falls over.
    private sealed class Child : IDisposable
    {
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

        private readonly ProxyCommand _command;

        private readonly TimeSpan _pause;

        private readonly CancellationTokenSource _stop = new();

        private readonly Lock _sync = new();

        private Process? _process;

        private string _fault = string.Empty;

        public Child(ProxyCommand command, TimeSpan pause)
        {
            _command = command;
            _pause = pause;
        }

        public ProxyState State
        {
            get
            {
                lock (_sync)
                {
                    return _process is { HasExited: false } ? ProxyState.Up : new ProxyState(false, _fault);
                }
            }
        }

        public ProxyState Start()
        {
            var token = _stop.Token;
            if (!Launch(token))
            {
                return State;
            }

            _ = KeepAsync(token);

            return ProxyState.Up;
        }

        public void Dispose()
        {
            _stop.Cancel();
            lock (_sync)
            {
                if (_process is { } process)
                {
                    Kill(process);
                    process.Dispose();
                    _process = null;
                }
            }

            _stop.Dispose();
        }

        private static void Kill(Process process)
        {
            try
            {
                process.Kill(true);
                process.WaitForExit(Grace);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        private async Task KeepAsync(CancellationToken token)
        {
            while (Current() is { } process)
            {
                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    Ended(process);
                    await Task.Delay(_pause, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                if (!Launch(token))
                {
                    return;
                }
            }
        }

        private Process? Current()
        {
            lock (_sync)
            {
                return _process;
            }
        }

        private void Ended(Process process)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _fault = $"{Path.GetFileName(_command.File)} ended with code {process.ExitCode}";
                    _process = null;
                    process.Dispose();
                }
            }
        }

        private bool Launch(CancellationToken token)
        {
            lock (_sync)
            {
                if (token.IsCancellationRequested)
                {
                    return false;
                }

                var start = new ProcessStartInfo { FileName = _command.File, UseShellExecute = false };
                foreach (var argument in _command.Arguments)
                {
                    start.ArgumentList.Add(argument);
                }

                try
                {
                    _process = Process.Start(start);
                    _fault = _process is null ? $"the host did not start {_command.File}" : string.Empty;
                }
                catch (Win32Exception ex)
                {
                    _fault = $"{_command.File}: {ex.Message}";
                }

                return _process is not null;
            }
        }
    }
}
