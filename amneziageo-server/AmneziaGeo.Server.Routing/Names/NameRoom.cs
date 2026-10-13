namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Tells when the panel carries as many connections as it takes and when it has room again.
/// </summary>
public static class NameRoom
{
    /// <summary>
    /// Tells whether the panel stays out of new connections: it is full at the most it carries, and has room again
    /// once a quarter of them ended.
    /// </summary>
    public static bool Full(bool full, long open, int most)
    {
        return full ? open > most - (most / 4) : open >= most;
    }
}
