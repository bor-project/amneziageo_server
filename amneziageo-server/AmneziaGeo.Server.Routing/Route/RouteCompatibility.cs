using System.Text.Json;

namespace AmneziaGeo.Server.Routing.Route;

/// <summary>
/// Compares routing configurations.
/// </summary>
public static class RouteCompatibility
{
    /// <summary>
    /// Compares ordered routing conditions.
    /// </summary>
    public static bool Same(IReadOnlyList<RouteRule> before, IReadOnlyList<RouteRule> after) =>
        before.Count == after.Count && before.Zip(after).All(pair => Shape(pair.First) == Shape(pair.Second));

    private static string Shape(RouteRule rule) => JsonSerializer.Serialize(new
    {
        rule.Id,
        Name = rule.Id > 0 ? rule.Name : string.Empty,
        rule.Position,
        rule.IsEnabled,
        rule.Action,
        rule.Outbound,
        rule.Targets,
        rule.Groups,
        rule.Clients,
        rule.Inbounds,
        rule.Ports,
        rule.SourcePorts,
        rule.Protocol,
    });
}
