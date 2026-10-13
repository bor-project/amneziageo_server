namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Watches that what the firewall hands over reaches the panel.
/// </summary>
public sealed class NameWatch
{
    private long? _handed;

    private long _reached;

    private bool _doubted;

    /// <summary>
    /// How many packets were handed over since a connection reached the panel last.
    /// </summary>
    public long Unreached { get; private set; }

    /// <summary>
    /// Starts the watch over from the connections taken so far.
    /// </summary>
    public void Start(long taken)
    {
        _handed = null;
        _reached = taken;
        _doubted = false;
        Unreached = 0;
    }

    /// <summary>
    /// Takes how many packets the firewall handed over and how many connections the panel took so far, telling
    /// whether the host drops what is handed over: packets went and no connection came across two looks in a row.
    /// </summary>
    public bool Look(long? handed, long taken)
    {
        var took = taken - _reached;
        _reached = taken;
        Unreached = took > 0 || handed is null ? 0 : Unreached + Since(_handed, handed.Value);
        _handed = handed;
        if (Unreached < NameDefaults.Unreached)
        {
            _doubted = false;

            return false;
        }

        if (!_doubted)
        {
            _doubted = true;

            return false;
        }

        return true;
    }

    // Returns how much a counter grew since it was read last; a counter that started over counts from nothing.
    private static long Since(long? before, long now) => before is { } was && now >= was ? now - was : now;
}
