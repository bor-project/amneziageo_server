namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the subscriptions are served with when nothing is set.
/// </summary>
public static class SubscriptionDefaults
{
    /// <summary>
    /// The port the subscriptions are served on.
    /// </summary>
    public const int Port = 2096;

    /// <summary>
    /// The path the subscriptions sit under.
    /// </summary>
    public const string Path = "sub";

    /// <summary>
    /// How often a client reads the subscription again, in hours.
    /// </summary>
    public const int UpdateHours = 12;

    /// <summary>
    /// The settings the subscriptions start with when the panel holds none.
    /// </summary>
    public static readonly SubscriptionSettings Settings = new();
}
