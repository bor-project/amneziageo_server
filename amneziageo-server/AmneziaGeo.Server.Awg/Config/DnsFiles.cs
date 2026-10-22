namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// What the server picks for the name servers of the files of its clients, unless a template or a client picks otherwise.
/// </summary>
/// <param name="Mode">What the server picks.</param>
/// <param name="Servers">The name servers written when it picks its own.</param>
public sealed record DnsFiles(DnsChoice Mode, IReadOnlyList<string> Servers)
{
    /// <summary>
    /// The pick of a server that never chose: the resolver of the panel while the routing needs it.
    /// </summary>
    public static DnsFiles Usual { get; } = new(DnsChoice.Routing, []);
}
