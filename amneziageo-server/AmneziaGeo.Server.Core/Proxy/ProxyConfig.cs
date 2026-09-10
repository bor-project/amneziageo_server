namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// A proxy the interfaces of the host are reachable through.
/// </summary>
public sealed record ProxyConfig
{
    /// <summary>
    /// The number the panel holds the proxy under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name of the proxy, which its service and its files are named after.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The way the proxy takes tunnels in.
    /// </summary>
    public string Kind { get; init; } = ProxyKind.Ws;

    /// <summary>
    /// Whether the host runs the proxy.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// The port the proxy listens on.
    /// </summary>
    public int Port { get; init; } = ProxyDefaults.Port;

    /// <summary>
    /// The path a tunnel names to reach the proxy.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Where the proxy passes the datagrams on, as host and port.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// The addresses and the networks the proxy takes, empty for any.
    /// </summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// The certificate chain the proxy answers under, empty for the one of the panel.
    /// </summary>
    public string Certificate { get; init; } = string.Empty;

    /// <summary>
    /// The key of the certificate the proxy answers under.
    /// </summary>
    public string CertificateKey { get; init; } = string.Empty;
}
