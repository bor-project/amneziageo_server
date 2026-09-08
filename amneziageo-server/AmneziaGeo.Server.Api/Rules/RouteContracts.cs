using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// What the host carries for one rule.
/// </summary>
public sealed record RouteStateBody(bool IsLive, string Fault, string Message, uint Mark, int Ranges, int Names);

/// <summary>
/// One routing rule as the panel reads it.
/// </summary>
public sealed record RouteResponse(
    long Id,
    string Name,
    int Position,
    bool IsEnabled,
    string Action,
    string Outbound,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Ports,
    string Protocol,
    RouteStateBody State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The settings a rule is added or changed with.
/// </summary>
public sealed record RouteRequest(
    string? Name,
    bool IsEnabled,
    string? Action,
    string? Outbound,
    IReadOnlyList<string>? Targets,
    IReadOnlyList<string>? Sources,
    IReadOnlyList<string>? Ports,
    string? Protocol);

/// <summary>
/// Which way a rule moves in the list.
/// </summary>
public sealed record RouteMoveRequest(bool Up);

/// <summary>
/// Whether a rule is on.
/// </summary>
public sealed record RouteSwitchRequest(bool On);

/// <summary>
/// The rules as the host takes them.
/// </summary>
public sealed record RulesetResponse(string Text);

/// <summary>
/// Turns rules into what the panel reads and back.
/// </summary>
public static class RouteAnswers
{
    private static readonly char[] Breaks = [',', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Returns one rule with what the host carries for it.
    /// </summary>
    public static RouteResponse Rule(RouteRule rule, RouteLeg? leg)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new RouteResponse(
            rule.Id,
            rule.Name,
            rule.Position,
            rule.IsEnabled,
            rule.Action,
            rule.Outbound,
            rule.Targets,
            rule.Sources,
            rule.Ports,
            rule.Protocol,
            State(leg),
            rule.CreatedUtc,
            rule.UpdatedUtc);
    }

    /// <summary>
    /// Returns what a request asks a rule to become.
    /// </summary>
    public static RouteRule Draft(RouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RouteRule
        {
            Name = (request.Name ?? string.Empty).Trim(),
            IsEnabled = request.IsEnabled,
            Action = (request.Action ?? RouteAction.Out).Trim(),
            Outbound = (request.Outbound ?? string.Empty).Trim(),
            Targets = Clean(request.Targets),
            Sources = Clean(request.Sources),
            Ports = Clean(request.Ports),
            Protocol = (request.Protocol ?? RouteProtocol.Any).Trim(),
        };
    }

    private static RouteStateBody State(RouteLeg? leg) => leg is null
        ? new RouteStateBody(false, string.Empty, string.Empty, 0, 0, 0)
        : new RouteStateBody(
            leg.IsLive,
            leg.Fault?.Code ?? string.Empty,
            leg.Fault?.Message ?? string.Empty,
            leg.Mark,
            leg.Cidrs4.Count + leg.Cidrs6.Count,
            leg.Domains.Count);

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
    [
        .. (values ?? [])
            .SelectMany(value => value.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    ];
}
