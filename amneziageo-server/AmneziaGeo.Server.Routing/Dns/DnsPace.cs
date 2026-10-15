namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// How long the resolver rests: until the soonest of the moments its work falls due, from half a second to a minute.
/// </summary>
public static class DnsPace
{
    /// <summary>
    /// The shortest rest.
    /// </summary>
    public static readonly TimeSpan Least = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest rest.
    /// </summary>
    public static readonly TimeSpan Most = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Returns how long the resolver rests before the soonest of the moments given.
    /// </summary>
    public static TimeSpan Rest(DateTimeOffset now, params ReadOnlySpan<DateTimeOffset> due)
    {
        var soonest = now + Most;
        foreach (var one in due)
        {
            if (one < soonest)
            {
                soonest = one;
            }
        }

        var rest = soonest - now;

        return rest < Least ? Least : rest;
    }
}
