using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// What the host carries for one rule.
/// </summary>
public sealed record RouteStateBody(
    bool IsLive,
    bool IsHeld,
    string Fault,
    string Message,
    uint Mark,
    int Ranges,
    int Names);

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
    bool HoldsWhenDown,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> Inbounds,
    IReadOnlyList<string> Ports,
    IReadOnlyList<string> SourcePorts,
    string Protocol,
    RouteStateBody State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The settings a rule is added or changed with.
/// </summary>
public sealed record RouteRequest(
    string? Name,
    bool? IsEnabled,
    string? Action,
    string? Outbound,
    bool? HoldsWhenDown,
    IReadOnlyList<string>? Targets,
    IReadOnlyList<string>? Sources,
    IReadOnlyList<string>? Ports,
    string? Protocol,
    IReadOnlyList<string>? Clients = null,
    IReadOnlyList<string>? Inbounds = null,
    IReadOnlyList<string>? SourcePorts = null);

/// <summary>
/// Which way a rule moves in the list, or the place it goes to, counting from one.
/// </summary>
public sealed record RouteMoveRequest(bool Up, int? To = null);

/// <summary>
/// The basic lists as the panel reads them.
/// </summary>
public sealed record RouteBasicResponse(
    IReadOnlyList<string> Direct,
    IReadOnlyList<string> Block,
    RouteStateBody DirectState,
    RouteStateBody BlockState,
    DateTimeOffset UpdatedUtc);

/// <summary>
/// The basic lists a request sets, keeping the one it stays silent about.
/// </summary>
public sealed record RouteBasicRequest(IReadOnlyList<string>? Direct, IReadOnlyList<string>? Block);

/// <summary>
/// The traffic the route tester is asked about.
/// </summary>
public sealed record RouteTestRequest(string? Target, int? Port, string? Protocol, string? Client, int? SourcePort);

/// <summary>
/// The rule that took the traffic in the answer of the route tester.
/// </summary>
public sealed record RouteTestRule(long Id, string Name, int Place, string Action, string Outbound);

/// <summary>
/// One outbound the traffic may leave through in the answer of the route tester.
/// </summary>
public sealed record RouteTestMember(string Name, bool IsEnabled, bool Carries, bool IsPicked);

/// <summary>
/// The way out of the rule that took the traffic.
/// </summary>
public sealed record RouteTestExit(string Name, bool IsGroup, string Strategy, IReadOnlyList<RouteTestMember> Members);

/// <summary>
/// Where the rules send the traffic asked about.
/// </summary>
public sealed record RouteTestResponse(
    string Verdict,
    string Guard,
    string Name,
    IReadOnlyList<string> Addresses,
    string Inbound,
    IReadOnlyList<string> Sources,
    RouteTestRule? Rule,
    RouteTestExit? Exit,
    IReadOnlyList<RouteStep> Steps);

/// <summary>
/// Whether a rule is on.
/// </summary>
public sealed record RouteSwitchRequest(bool? On);

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
            rule.HoldsWhenDown,
            rule.Targets,
            rule.Sources,
            rule.Clients,
            rule.Inbounds,
            rule.Ports,
            rule.SourcePorts,
            rule.Protocol,
            State(leg),
            rule.CreatedUtc,
            rule.UpdatedUtc);
    }

    /// <summary>
    /// Returns what a request asks a rule to become, keeping what it stays silent about.
    /// </summary>
    public static RouteRule Draft(RouteRequest request, RouteRule? held = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RouteRule
        {
            Name = (request.Name ?? string.Empty).Trim(),
            IsEnabled = request.IsEnabled ?? held?.IsEnabled ?? false,
            Action = (request.Action ?? RouteAction.Out).Trim(),
            Outbound = (request.Outbound ?? string.Empty).Trim(),
            HoldsWhenDown = request.HoldsWhenDown ?? held?.HoldsWhenDown ?? true,
            Targets = Clean(request.Targets),
            Sources = Clean(request.Sources),
            Clients = request.Clients is null ? held?.Clients ?? [] : Clean(request.Clients),
            Inbounds = request.Inbounds is null ? held?.Inbounds ?? [] : Clean(request.Inbounds),
            Ports = Clean(request.Ports),
            SourcePorts = request.SourcePorts is null ? held?.SourcePorts ?? [] : Clean(request.SourcePorts),
            Protocol = (request.Protocol ?? RouteProtocol.Any).Trim(),
        };
    }

    /// <summary>
    /// Returns what a request asks the basic lists to become, keeping the one it stays silent about.
    /// </summary>
    public static RouteBasic Basic(RouteBasicRequest request, RouteBasic held)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(held);

        return held with
        {
            Direct = request.Direct is null ? held.Direct : Clean(request.Direct),
            Block = request.Block is null ? held.Block : Clean(request.Block),
        };
    }

    /// <summary>
    /// Returns the basic lists with what the host carries for them.
    /// </summary>
    public static RouteBasicResponse Basic(RouteBasic basic, RoutePlan plan)
    {
        ArgumentNullException.ThrowIfNull(basic);
        ArgumentNullException.ThrowIfNull(plan);

        return new RouteBasicResponse(
            basic.Direct,
            basic.Block,
            State(plan.Legs.FirstOrDefault(leg => leg.Rule.Id == RouteBasic.DirectId)),
            State(plan.Legs.FirstOrDefault(leg => leg.Rule.Id == RouteBasic.BlockId)),
            basic.UpdatedUtc);
    }

    private static RouteStateBody State(RouteLeg? leg) => leg is null
        ? new RouteStateBody(false, false, string.Empty, string.Empty, 0, 0, 0)
        : new RouteStateBody(
            leg.IsLive,
            leg.IsHeld,
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
