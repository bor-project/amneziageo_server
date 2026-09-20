namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// The proxy template a fresh database starts with.
/// </summary>
public static class ProxyTemplateDefaults
{
    /// <summary>
    /// The name the built in template takes.
    /// </summary>
    public const string Name = "websocket";

    /// <summary>
    /// Returns the built in template.
    /// </summary>
    public static ProxyTemplate Fresh() => new()
    {
        Name = Name,
        Kind = ProxyKind.Ws,
        Port = ProxyDefaults.Port,
        MakePath = true,
    };
}
