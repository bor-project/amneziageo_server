namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Keeps whether the host changed its interfaces or its routing rules since the round last looked, and wakes the round
/// when it did; a host the panel does not hear counts as changed every round.
/// </summary>
public sealed class HostChanges : IDisposable
{
    /// <summary>
    /// How long the round lets a burst of news settle before it looks.
    /// </summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _told = new(0, 1);

    private readonly TimeProvider _time;

    private int _moved = 1;

    private volatile bool _deaf = true;

    /// <summary>
    /// ctor
    /// </summary>
    public HostChanges(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>
    /// Whether the panel hears the kernel about the host.
    /// </summary>
    public bool Hears => !_deaf;

    /// <summary>
    /// Takes the news that the kernel will be heard from now on.
    /// </summary>
    public void Listening() => _deaf = false;

    /// <summary>
    /// Takes the news that the kernel is no longer heard.
    /// </summary>
    public void Deaf()
    {
        _deaf = true;
        Wake();
    }

    /// <summary>
    /// Takes the news that the host changed.
    /// </summary>
    public void Heard()
    {
        Interlocked.Exchange(ref _moved, 1);
        Wake();
    }

    /// <summary>
    /// Tells whether the host changed since the last time asked, and starts over.
    /// </summary>
    public bool Take() => Interlocked.Exchange(ref _moved, 0) == 1 || _deaf;

    /// <summary>
    /// Waits for the round or for news of a change, whichever comes first; news is given a moment to settle.
    /// </summary>
    public async Task WaitAsync(TimeSpan round, CancellationToken ct)
    {
        if (await _told.WaitAsync(round, ct).ConfigureAwait(false))
        {
            await Task.Delay(Settle, _time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lets the signal go.
    /// </summary>
    public void Dispose() => _told.Dispose();

    private void Wake()
    {
        try
        {
            _told.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
}
