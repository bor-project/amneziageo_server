namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Where the websocket fronts of the endpoints run.
/// </summary>
public static class ProxyDefaults
{
    /// <summary>
    /// Where the files of the fronts are written.
    /// </summary>
    public const string Directory = "/etc/amneziageo-server";

    /// <summary>
    /// The service a front runs as, with the name of the interface after the at sign.
    /// </summary>
    public const string Service = "amneziageo-proxy";

    /// <summary>
    /// The service the relays of the releases before ran as, with their name after the at sign.
    /// </summary>
    public const string RelayService = "amneziageo-relay";

    /// <summary>
    /// The host a front passes the datagrams on to.
    /// </summary>
    public const string Loopback = "127.0.0.1";
}
