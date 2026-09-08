namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// The kinds of record a name server answers with.
/// </summary>
public enum DnsRecordType
{
    Unknown = 0,
    A = 1,
    Cname = 5,
    Aaaa = 28,
    Https = 65,
}
