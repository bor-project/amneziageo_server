namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Names the rule resolver selections.
/// </summary>
public static class TrafficDnsMode
{
    public const string Default = "default";
    public const string Custom = "custom";

    public static bool Known(string? value) => value is Default or Custom;
}
