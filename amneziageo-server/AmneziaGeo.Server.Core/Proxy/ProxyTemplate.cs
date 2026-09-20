namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// The settings every proxy of a template carries, as the panel holds them.
/// </summary>
public sealed record ProxyTemplate
{
    /// <summary>
    /// The number the panel holds the template under.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// The name the template is listed under.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The way the proxies take tunnels in.
    /// </summary>
    public string Kind { get; init; } = ProxyKind.Ws;

    /// <summary>
    /// The port a fresh proxy of the template starts from.
    /// </summary>
    public int Port { get; init; } = ProxyDefaults.Port;

    /// <summary>
    /// Whether the panel holds the port of the proxies open in the firewall of the host.
    /// </summary>
    public bool Opened { get; init; }

    /// <summary>
    /// Whether a proxy of the template takes a path no one guesses.
    /// </summary>
    public bool MakePath { get; init; } = true;

    /// <summary>
    /// Where the proxies pass the datagrams on, as host and port.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// The addresses and the networks the proxies take, empty for any.
    /// </summary>
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// When the template was added.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the template was last changed.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>
    /// Returns the proxy with the constants of the template in place of its own.
    /// </summary>
    public ProxyConfig Over(ProxyConfig proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return proxy with
        {
            TemplateId = Id,
            Kind = Kind,
            Opened = Opened,
            Target = Target,
            Sources = [.. Sources],
            Path = Way(proxy.Path),
        };
    }

    /// <summary>
    /// Returns a proxy of the template under a name, on the port the template starts from.
    /// </summary>
    public ProxyConfig Fresh(string name) => Over(new ProxyConfig { Name = name, Port = Port });

    /// <summary>
    /// Returns the template the settings of a proxy amount to.
    /// </summary>
    public static ProxyTemplate Of(string name, ProxyConfig proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        return new ProxyTemplate
        {
            Name = name,
            Kind = proxy.Kind,
            Port = proxy.Port,
            Opened = proxy.Opened,
            MakePath = ProxyKind.HasPath(proxy.Kind),
            Target = proxy.Target,
            Sources = [.. proxy.Sources],
        };
    }

    private string Way(string held)
    {
        if (!ProxyKind.HasPath(Kind))
        {
            return string.Empty;
        }

        return held.Length > 0 || !MakePath ? held : ProxyDefaults.Secret();
    }
}
