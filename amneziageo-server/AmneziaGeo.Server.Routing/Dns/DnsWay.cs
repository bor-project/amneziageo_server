namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// The way out a question of the resolver takes.
/// </summary>
/// <param name="Mark">The mark the question carries, zero for the way out of the host, null when it is not asked.</param>
/// <param name="Outbound">The outbound the question leaves through, empty for the host and when it is not asked.</param>
/// <param name="Fault">What is wrong with the way out, or null.</param>
public sealed record DnsWay(uint? Mark, string Outbound, DnsFault? Fault)
{
    /// <summary>
    /// The way out of the host itself.
    /// </summary>
    public static readonly DnsWay Host = new(0u, string.Empty, null);

    /// <summary>
    /// Returns the way no question takes.
    /// </summary>
    public static DnsWay Closed(string code, string message) => new(null, string.Empty, new DnsFault(code, message));
}
