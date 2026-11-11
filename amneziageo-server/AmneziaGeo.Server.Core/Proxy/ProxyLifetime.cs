namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Holds the users of a proxy generation.
/// </summary>
public sealed class ProxyLifetime : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _source = new();
    private readonly CancellationToken _stopped;
    private int _users;
    private bool _retired;
    private bool _canceling;
    private bool _disposed;

    /// <summary>
    /// ctor
    /// </summary>
    public ProxyLifetime() => _stopped = _source.Token;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return !_retired;
            }
        }
    }

    /// <summary>
    /// Takes one user of the generation.
    /// </summary>
    public bool TryLease(out ProxyLease lease)
    {
        lock (_sync)
        {
            lease = default!;
            if (_retired)
            {
                return false;
            }

            _users++;
            lease = new ProxyLease(_stopped, Release);
            return true;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            _canceling = true;
        }

        try
        {
            _source.Cancel();
        }
        finally
        {
            lock (_sync)
            {
                _canceling = false;
                Finish();
            }
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _users--;
            Finish();
        }
    }

    private void Finish()
    {
        if (_retired && !_canceling && _users == 0 && !_disposed)
        {
            _disposed = true;
            _source.Dispose();
        }
    }
}

/// <summary>
/// Keeps the cancellation token of a proxy generation.
/// </summary>
public sealed class ProxyLease : IDisposable
{
    private Action? _release;

    /// <summary>
    /// ctor
    /// </summary>
    internal ProxyLease(CancellationToken stopped, Action release)
    {
        Stopped = stopped;
        _release = release;
    }

    public CancellationToken Stopped { get; }

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
