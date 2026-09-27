namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Tells the ruleset whether the new connections go to the log, and keeps what became of the log.
/// </summary>
public sealed class AccessGate
{
    private int _open;

    private string? _fault;

    /// <summary>
    /// The group the ruleset logs to, or null while nothing takes the records.
    /// </summary>
    public ushort? Group => Volatile.Read(ref _open) == 1 ? AccessDefaults.Group : null;

    /// <summary>
    /// Why the log does not run although it is turned on, or null.
    /// </summary>
    public string? Fault => Volatile.Read(ref _fault);

    /// <summary>
    /// Lets the ruleset log the new connections.
    /// </summary>
    public void Open()
    {
        Volatile.Write(ref _fault, null);
        Volatile.Write(ref _open, 1);
    }

    /// <summary>
    /// Keeps the ruleset from logging, with the reason when the log failed.
    /// </summary>
    public void Close(string? fault = null)
    {
        Volatile.Write(ref _open, 0);
        Volatile.Write(ref _fault, fault);
    }
}
