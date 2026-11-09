using AmneziaGeo.Server.Core.Proxy;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Routing.XHttp;

/// <summary>
/// Reports the state of an XHTTP child.
/// </summary>
public sealed record XHttpState(bool IsRunning, string Fault);

/// <summary>
/// Runs the HTTP GET children of the server.
/// </summary>
public sealed class XHttpHost : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);
    private readonly string _directory;
    private readonly string _executable;
    private readonly ILogger<XHttpHost> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<long, Child> _children = new();
    private readonly ConcurrentDictionary<long, XHttpState> _states = new();
    private volatile IReadOnlyDictionary<long, string> _desired = new Dictionary<long, string>();
    private readonly Action<Process> _kill;
    private bool _disposed;

    /// <summary>
    /// ctor
    /// </summary>
    public XHttpHost(string directory, string executable, ILogger<XHttpHost> logger)
        : this(directory, executable, logger, process => process.Kill(true))
    {
    }

    /// <summary>
    /// ctor
    /// </summary>
    internal XHttpHost(string directory, string executable, ILogger<XHttpHost> logger, Action<Process> kill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = Path.GetFullPath(directory);
        _executable = executable;
        _logger = logger;
        _kill = kill ?? throw new ArgumentNullException(nameof(kill));
    }

    /// <summary>
    /// Returns the state of a listener.
    /// </summary>
    public string ConfigurationDirectory => _directory;

    public XHttpState State(long id)
    {
        if (_children.TryGetValue(id, out var child))
        {
            return child.IsRunning
                ? new XHttpState(true, child.Fault)
                : new XHttpState(false, child.ExitFault());
        }

        return _states.TryGetValue(id, out var state) ? state : new XHttpState(false, string.Empty);
    }

    /// <summary>
    /// Reports whether the current configuration serves requests.
    /// </summary>
    public bool CanRelay(long id)
    {
        var desired = _desired;
        return desired.TryGetValue(id, out var text) && _children.TryGetValue(id, out var child) && child.CanRelay(text);
    }

    /// <summary>
    /// Takes the lifetime of the current configuration.
    /// </summary>
    public bool TryLease(long id, out ProxyLease lease)
    {
        lease = default!;
        var desired = _desired;
        if (!desired.TryGetValue(id, out var text) || !_children.TryGetValue(id, out var child)
            || !child.CanRelay(text))
        {
            return false;
        }

        if (!child.TryLease(out lease))
        {
            return false;
        }

        if (!ReferenceEquals(desired, _desired))
        {
            lease.Dispose();
            lease = default!;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the desired XHTTP listeners.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> ApplyAsync(IReadOnlyList<XHttpProxy> proxies, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(proxies);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var faults = new Dictionary<long, string>();
            var desired = new Dictionary<long, (XHttpProxy Proxy, string Text)>();
            foreach (var group in proxies.GroupBy(proxy => proxy.Id))
            {
                if (group.Count() != 1)
                {
                    faults[group.Key] = "The listener ID is repeated.";
                    continue;
                }

                var proxy = group.First();
                try
                {
                    desired[proxy.Id] = (proxy, XHttpFile.Render(proxy));
                }
                catch (ArgumentException ex)
                {
                    if (!_states.TryGetValue(proxy.Id, out var previous) || previous.Fault != ex.Message)
                    {
                        _logger.LogWarning(ex, "The XHTTP listener {ProxyId} was rejected", proxy.Id);
                    }

                    faults[proxy.Id] = ex.Message;
                }
            }

            _desired = desired.ToDictionary(pair => pair.Key, pair => pair.Value.Text);
            foreach (var (id, child) in _children)
            {
                if (!desired.TryGetValue(id, out var entry) || entry.Text != child.Text)
                {
                    child.Revoke();
                }
            }

            foreach (var id in _children.Keys.Where(id => !desired.ContainsKey(id)).ToArray())
            {
                var fault = await StopAsync(id).ConfigureAwait(false);
                if (fault.Length > 0)
                {
                    faults[id] = fault;
                }
            }

            foreach (var (id, entry) in desired.OrderBy(pair => pair.Key))
            {
                ct.ThrowIfCancellationRequested();
                if (_children.TryGetValue(id, out var present) && present.CanRelay(entry.Text))
                {
                    continue;
                }

                var stopped = await StopAsync(id).ConfigureAwait(false);
                if (stopped.Length > 0)
                {
                    faults[id] = stopped;
                    continue;
                }

                try
                {
                    var versionFault = await CheckVersionAsync(ct).ConfigureAwait(false);
                    if (versionFault.Length > 0)
                    {
                        faults[id] = versionFault;
                        continue;
                    }

                    var file = await WriteAsync(id, entry.Text, ct).ConfigureAwait(false);
                    var child = Start(entry.Proxy, entry.Text, file);
                    _children[id] = child;
                    await Task.Delay(TimeSpan.FromMilliseconds(150), ct).ConfigureAwait(false);
                    if (!child.IsRunning)
                    {
                        var fault = child.ExitFault();
                        await StopAsync(id).ConfigureAwait(false);
                        faults[id] = fault;
                    }
                    else
                    {
                        _states[id] = new XHttpState(true, string.Empty);
                    }
                }
                catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Applying the XHTTP listener {ProxyId} was canceled", id);
                    await StopAsync(id).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    var fault = $"The HTTP GET listener did not start: {ex.GetType().Name}.";
                    if (!_states.TryGetValue(id, out var previous) || previous.Fault != fault)
                    {
                        _logger.LogError(ex, "The XHTTP listener {ProxyId} did not start", id);
                    }

                    await StopAsync(id).ConfigureAwait(false);
                    if (!_children.ContainsKey(id))
                    {
                        Delete(Path.Combine(_directory, $"xhttp-{id}.json"));
                    }

                    faults[id] = fault;
                }
            }

            foreach (var (id, fault) in faults)
            {
                _states[id] = new XHttpState(false, fault);
            }

            return faults;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the HTTP GET children of the server.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed && _children.IsEmpty)
            {
                return;
            }

            _disposed = true;
            _desired = new Dictionary<long, string>();
            foreach (var child in _children.Values)
            {
                child.Revoke();
            }

            foreach (var id in _children.Keys.ToArray())
            {
                await StopAsync(id).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private ProcessStartInfo Command(params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private Child Start(XHttpProxy proxy, string text, string file)
    {
        var process = new Process { StartInfo = Command("get-server", "-c", file) };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("HTTP GET did not create a process.");
            }

            return new Child(process, text, file, proxy.Credentials, _logger, _kill);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating the XHTTP child {ProxyId} failed", proxy.Id);
            process.Dispose();
            throw;
        }
    }

    private async Task<string> CheckVersionAsync(CancellationToken ct)
    {
        using var process = new Process { StartInfo = Command("get-server", "--help") };
        if (!process.Start())
        {
            return "The HTTP GET version command did not start.";
        }

        var child = new Child(process, string.Empty, string.Empty, [], _logger);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(VersionTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await child.DrainAsync().ConfigureAwait(false);
            var output = child.Output;
            return process.ExitCode == 0 && output.Contains("get-server", StringComparison.Ordinal)
                ? string.Empty
                : $"wstunnel GET is required; the installed executable does not support get-server.";
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Reading the HTTP GET version timed out");
            return "The HTTP GET version command timed out.";
        }
        finally
        {
            await child.StopAsync().ConfigureAwait(false);
            if (!child.IsRunning)
            {
                child.Close();
            }
        }
    }

    private async Task<string> WriteAsync(long id, string text, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_directory);
        }
        else
        {
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var file = Path.Combine(_directory, $"xhttp-{id}.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var stream = new FileStream(temporary, options);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(text), ct).ConfigureAwait(false);
            }

            File.Move(temporary, file, true);
            return file;
        }
        finally
        {
            Delete(temporary);
        }
    }

    private async Task<string> StopAsync(long id)
    {
        if (!_children.TryGetValue(id, out var child))
        {
            return string.Empty;
        }

        child.Revoke();
        var before = child.IsRunning ? string.Empty : child.ExitFault();
        var fault = await child.StopAsync().ConfigureAwait(false);
        if (fault.Length > 0)
        {
            child.Fault = fault;
            return fault;
        }

        _children.TryRemove(id, out _);
        child.Close();
        Delete(child.File);
        _states[id] = new XHttpState(false, before);
        return string.Empty;
    }

    private void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Removing an XHTTP configuration file failed");
        }
    }

    private sealed class Child
    {
        private const int OutputLimit = 8192;
        private readonly ILogger _logger;
        private readonly string[] _credentials;
        private readonly StringBuilder _output = new();
        private readonly Lock _outputLock = new();
        private readonly Lock _processLock = new();
        private readonly CancellationTokenSource _readers = new();
        private readonly ProxyLifetime _relay = new();
        private readonly Action<Process> _kill;
        private readonly Task _stdout;
        private readonly Task _stderr;
        private bool _processDisposed;
        private int? _exitCode;

        /// <summary>
        /// ctor
        /// </summary>
        public Child(Process process, string text, string file, IReadOnlyList<string> credentials, ILogger logger, Action<Process>? kill = null)
        {
            Process = process;
            _kill = kill ?? (child => child.Kill(true));
            Text = text;
            File = file;
            _credentials = credentials.SelectMany(credential => Guid.TryParse(credential, out var id)
                    ? new[] { credential, id.ToString("D"), id.ToString("N"), id.ToString("B"), id.ToString("P") }
                    : [credential])
                .Where(credential => !string.IsNullOrEmpty(credential)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _logger = logger;
            _stdout = CaptureAsync(process.StandardOutput);
            _stderr = CaptureAsync(process.StandardError);
        }

        public Process Process { get; }
        public string Text { get; }
        public string File { get; }
        public string Fault { get; set; } = string.Empty;
        public bool CanRelay(string text) => Text == text && Fault.Length == 0 && _relay.IsActive && IsRunning;

        public bool TryLease(out ProxyLease lease) => _relay.TryLease(out lease);

        public void Revoke()
        {
            try
            {
                _relay.Dispose();
            }
            catch (AggregateException ex)
            {
                _logger.LogWarning(ex, "Canceling XHTTP streams failed");
            }
        }
        public bool IsRunning
        {
            get
            {
                lock (_processLock)
                {
                    return !_processDisposed && !Process.HasExited;
                }
            }
        }

        public string Output
        {
            get
            {
                lock (_outputLock)
                {
                    var output = _output.ToString();
                    foreach (var credential in _credentials)
                    {
                        output = output.Replace(credential, "[redacted]", StringComparison.OrdinalIgnoreCase);
                    }

                    return Regex.Replace(output, @"(?i)\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", "[redacted]");
                }
            }
        }

        public string ExitFault()
        {
            lock (_processLock)
            {
                var code = _processDisposed ? _exitCode : Process.HasExited ? Process.ExitCode : default(int?);
                return Fault.Length > 0 ? Fault : $"HTTP GET exited with code {code}: {Output.Trim()}";
            }
        }

        public void Close()
        {
            lock (_processLock)
            {
                if (_processDisposed)
                {
                    return;
                }

                _exitCode = Process.ExitCode;
                _processDisposed = true;
                Process.Dispose();
                _readers.Dispose();
                _relay.Dispose();
            }
        }

        public async Task DrainAsync()
        {
            try
            {
                await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                _logger.LogDebug(ex, "Reading the HTTP GET output timed out");
                _readers.Cancel();
            }
        }

        public async Task<string> StopAsync()
        {
            try
            {
                if (IsRunning)
                {
                    try
                    {
                        _kill(Process);
                    }
                    catch (InvalidOperationException ex) when (!IsRunning)
                    {
                        _logger.LogDebug(ex, "The HTTP GET child exited before termination");
                    }
                }

                using var timeout = new CancellationTokenSource(ShutdownTimeout);
                await Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await DrainAsync().ConfigureAwait(false);
                return string.Empty;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Stopping an HTTP GET child timed out");
                return "The HTTP GET child did not stop within five seconds.";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Stopping an HTTP GET child failed");
                return $"The HTTP GET child could not stop: {ex.GetType().Name}.";
            }
            finally
            {
                _readers.Cancel();
            }
        }

        private async Task CaptureAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            try
            {
                while (true)
                {
                    var count = await reader.ReadAsync(buffer.AsMemory(), _readers.Token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        return;
                    }

                    lock (_outputLock)
                    {
                        _output.Append(buffer, 0, count);
                        if (_output.Length > OutputLimit)
                        {
                            _output.Remove(0, _output.Length - OutputLimit);
                        }
                    }
                }
            }
            catch (OperationCanceledException ex) when (_readers.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Reading the HTTP GET output was canceled");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Reading the HTTP GET output failed");
            }
        }
    }
}
