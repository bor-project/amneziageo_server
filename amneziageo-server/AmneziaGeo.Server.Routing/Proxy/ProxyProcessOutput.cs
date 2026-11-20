using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// Drains process output and retains recognized failures.
/// </summary>
public sealed class ProxyProcessOutput : IDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Stream _stdout;
    private readonly Stream _stderr;
    private readonly ILogger _logger;
    private readonly Task _drained;
    private readonly Lock _sync = new();
    private Task<string>? _completion;
    private string _failure = string.Empty;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyProcessOutput(Process process, CancellationToken ct, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stdout = process.StandardOutput.BaseStream;
        _stderr = process.StandardError.BaseStream;
        var token = _stop.Token;
        var output = ObserveAsync(_stdout.CopyToAsync(Stream.Null, token));
        var error = ObserveAsync(ProxyProcessError.ReadAsync(process.StandardError, token, code => Volatile.Write(ref _failure, code)));
        _drained = Task.WhenAll(output, error);
        _ = _drained.ContinueWith(task => _logger.LogWarning(task.Exception, "The proxy output could not be drained"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task<string> CompleteAsync()
    {
        lock (_sync)
        {
            return _completion ??= FinishAsync();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _stop.Cancel();
            Close(_stdout);
            Close(_stderr);
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _stop.Dispose();
            }
        }
    }

    private async Task<string> FinishAsync()
    {
        if (await Task.WhenAny(_drained, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false) != _drained)
        {
            Stop();
            await Task.WhenAny(_drained, Task.Delay(TimeSpan.FromMilliseconds(100))).ConfigureAwait(false);
        }

        if (_drained.IsCompleted)
        {
            await _drained.ConfigureAwait(false);
        }

        return Volatile.Read(ref _failure);
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "A proxy output stream ended");
        }
    }

    private void Close(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "A proxy output stream could not be closed");
        }
    }
}
