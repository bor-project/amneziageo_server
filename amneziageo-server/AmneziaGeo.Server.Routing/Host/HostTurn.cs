namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Lets one change of what the panel holds on the host through at a time.
/// </summary>
public sealed class HostTurn : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Waits for the change that goes on and holds the turn until the hold is let go.
    /// </summary>
    public async Task<IDisposable> HoldAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        return new Hold(_gate);
    }

    /// <summary>
    /// Lets the gate go.
    /// </summary>
    public void Dispose() => _gate.Dispose();

    private sealed class Hold(SemaphoreSlim gate) : IDisposable
    {
        private int _gone;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _gone, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
