using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Where the name servers of a client file were picked.
/// </summary>
public enum DnsFrom
{
    /// <summary>
    /// The client picked them.
    /// </summary>
    Client = 0,

    /// <summary>
    /// The template of the client picked them.
    /// </summary>
    Template = 1,

    /// <summary>
    /// The server picked them.
    /// </summary>
    Server = 2,

    /// <summary>
    /// The template of the client routes by name and takes the resolver of the panel.
    /// </summary>
    StandIns = 3,
}

/// <summary>
/// The name servers a client file names, where they were picked and whether they are the resolver of the panel.
/// </summary>
/// <param name="Servers">The name servers of the DNS line.</param>
/// <param name="From">Where the choice was made.</param>
/// <param name="Choice">What was picked there.</param>
/// <param name="IsPanel">Whether the servers are the resolver of the panel.</param>
public sealed record ClientDns(IReadOnlyList<string> Servers, DnsFrom From, DnsChoice Choice, bool IsPanel);
