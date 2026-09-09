namespace AmneziaGeo.Server.Routing.Proxy;

/// <summary>
/// What the host answers about the proxy.
/// </summary>
/// <param name="IsRunning">Whether the service of the proxy is up.</param>
/// <param name="Message">What the host said when it refused.</param>
public sealed record ProxyState(bool IsRunning, string Message)
{
    /// <summary>
    /// The answer of a host that carries the proxy.
    /// </summary>
    public static readonly ProxyState Up = new(true, string.Empty);

    /// <summary>
    /// The answer of a host that carries no proxy.
    /// </summary>
    public static readonly ProxyState Down = new(false, string.Empty);
}
