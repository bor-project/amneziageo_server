namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Names the ports the services of an endpoint take.
/// </summary>
public static class ConfigServices
{
    /// <summary>
    /// The loopback port the websocket front of the first endpoint listens on.
    /// </summary>
    public const int FrontBase = 61000;

    /// <summary>
    /// How many loopback ports the websocket fronts take after the first.
    /// </summary>
    public const int FrontSpan = 4000;

    /// <summary>
    /// Returns the TCP port the services of an endpoint answer on.
    /// </summary>
    public static int Port(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.ServicesPort > 0 ? config.ServicesPort : config.ListenPort;
    }

    /// <summary>
    /// Tells whether the services of an endpoint answer on a port other than the one of the endpoint.
    /// </summary>
    public static bool Moved(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.ServicesPort > 0 && config.ServicesPort != config.ListenPort;
    }

    /// <summary>
    /// Returns the loopback port the websocket front of an endpoint listens on.
    /// </summary>
    public static int Front(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return FrontBase + (int)(config.Id % FrontSpan);
    }
}
