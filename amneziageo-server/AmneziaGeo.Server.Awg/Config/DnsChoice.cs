namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Where the name servers of a client file come from.
/// </summary>
public enum DnsChoice
{
    /// <summary>
    /// What the level above picks: the template for a client, the server for a template.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// The resolver of the panel.
    /// </summary>
    Panel = 1,

    /// <summary>
    /// The name servers written next to the choice.
    /// </summary>
    Own = 2,

    /// <summary>
    /// No name servers: the device asks the ones of its network.
    /// </summary>
    Network = 3,

    /// <summary>
    /// The name servers of the endpoint.
    /// </summary>
    Endpoint = 4,

    /// <summary>
    /// The resolver of the panel while the routing needs it, the name servers of the endpoint otherwise.
    /// </summary>
    Routing = 5,
}

/// <summary>
/// Writes and reads where the name servers of a client file come from.
/// </summary>
public static class DnsChoiceName
{
    /// <summary>
    /// The name of leaving the choice to the level above.
    /// </summary>
    public const string Auto = "auto";

    /// <summary>
    /// The name of picking the resolver of the panel.
    /// </summary>
    public const string Panel = "panel";

    /// <summary>
    /// The name of picking name servers of its own.
    /// </summary>
    public const string Own = "own";

    /// <summary>
    /// The name of leaving the name servers to the network of the device.
    /// </summary>
    public const string Network = "network";

    /// <summary>
    /// The name of picking the name servers of the endpoint.
    /// </summary>
    public const string Endpoint = "endpoint";

    /// <summary>
    /// The name of picking the resolver of the panel while the routing needs it.
    /// </summary>
    public const string Routing = "routing";

    /// <summary>
    /// Returns the name of a choice.
    /// </summary>
    public static string Of(DnsChoice choice) => choice switch
    {
        DnsChoice.Panel => Panel,
        DnsChoice.Own => Own,
        DnsChoice.Network => Network,
        DnsChoice.Endpoint => Endpoint,
        DnsChoice.Routing => Routing,
        _ => Auto,
    };

    /// <summary>
    /// Returns what a name stands for, the given choice where the name is empty and -1 where it is unknown.
    /// </summary>
    public static DnsChoice Read(string? text, DnsChoice otherwise)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return otherwise;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            Auto => DnsChoice.Auto,
            Panel => DnsChoice.Panel,
            Own => DnsChoice.Own,
            Network => DnsChoice.Network,
            Endpoint => DnsChoice.Endpoint,
            Routing => DnsChoice.Routing,
            _ => (DnsChoice)(-1),
        };
    }

    /// <summary>
    /// Returns what a name stands for at the server, where auto stands for routing.
    /// </summary>
    public static DnsChoice ReadServer(string? text, DnsChoice otherwise)
    {
        var choice = Read(text, otherwise);

        return choice == DnsChoice.Auto ? DnsChoice.Routing : choice;
    }

    /// <summary>
    /// Tells whether a choice is one the panel knows.
    /// </summary>
    public static bool IsKnown(DnsChoice choice) => choice is >= DnsChoice.Auto and <= DnsChoice.Routing;

    /// <summary>
    /// Tells whether a choice is one the server takes.
    /// </summary>
    public static bool IsServer(DnsChoice choice) => choice is > DnsChoice.Auto and <= DnsChoice.Routing;

    /// <summary>
    /// Returns the names of the choices a client or a template takes, for a refusal.
    /// </summary>
    public static string Listed() => $"'{Auto}', '{Routing}', '{Panel}', '{Network}', '{Own}' or '{Endpoint}'";

    /// <summary>
    /// Returns the names of the choices the server takes, for a refusal.
    /// </summary>
    public static string ListedServer() => $"'{Routing}', '{Panel}', '{Network}', '{Own}' or '{Endpoint}'";
}
