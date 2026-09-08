namespace AmneziaGeo.Server.Routing.Balance;

/// <summary>
/// The outbounds the host was last seen carrying traffic through.
/// </summary>
public sealed class BalanceLive
{
    private IReadOnlySet<string>? _alive;

    /// <summary>
    /// The outbounds that carry traffic, or null when the host was not read yet.
    /// </summary>
    public IReadOnlySet<string>? Alive => Volatile.Read(ref _alive);

    /// <summary>
    /// Keeps the outbounds that carry traffic and tells whether they are other than before.
    /// </summary>
    public bool Keep(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var fresh = new HashSet<string>(names, StringComparer.Ordinal);
        var held = Volatile.Read(ref _alive);
        Volatile.Write(ref _alive, fresh);

        return held is null || !fresh.SetEquals(held);
    }

    /// <summary>
    /// Drops what the host was seen carrying.
    /// </summary>
    public void Forget() => Volatile.Write(ref _alive, null);
}
