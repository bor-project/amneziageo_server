namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Names resolver selection modes.
/// </summary>
public static class DnsSelection
{
    public const string Global = "global";

    public const string Outbound = "outbound";

    public const string Routing = "routing";

    public static bool Known(string value) => value is Global or Outbound or Routing;
}
