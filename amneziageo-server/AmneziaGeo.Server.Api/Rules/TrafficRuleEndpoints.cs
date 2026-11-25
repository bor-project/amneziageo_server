using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// Manages reusable traffic rules.
/// </summary>
public static class TrafficRuleEndpoints
{
    public static IEndpointRouteBuilder MapTrafficRules(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/traffic-rules").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/dns-defaults", DnsDefaultsAsync);
        reading.MapGet("/{id:long}", FindAsync);
        var writing = routes.MapGroup("/api/traffic-rules").RequireScope(Scopes.ManageRouting);
        writing.MapGet("/draft", () => Results.Ok(new TrafficRule { Name = "addresses", DnsMode = TrafficDnsMode.Default }));
        writing.MapPost("/", AddAsync);
        writing.MapPost("/defaults", RestoreAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);
        return routes;
    }

    private static async Task<IResult> DnsDefaultsAsync(RouteApplier applier, CancellationToken ct) =>
        Results.Ok(new { Servers = (await applier.ReadAsync(ct).ConfigureAwait(false)).HostDns });

    private static async Task<IResult> RestoreAsync(RoutingDefaultsStore store, CancellationToken ct) =>
        Results.Ok(await store.RestoreRulesAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> ListAsync(TrafficRuleStore store, CancellationToken ct) =>
        Results.Ok(await store.ListAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> FindAsync(long id, TrafficRuleStore store, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        return found is null ? Results.NotFound(new Failure("unknown-traffic-rule", "the traffic rule does not exist")) : Results.Ok(found);
    }

    private static async Task<IResult> AddAsync(TrafficRuleRequest request, TrafficRuleStore store, RouteApplier applier, CancellationToken ct)
    {
        if (Invalid(request) is { } invalid)
        {
            return invalid;
        }

        var draft = Draft(request);
        if (draft.Targets.Count == 0)
        {
            return Results.BadRequest(new Failure("empty-addresses", "specify addresses or domains"));
        }

        var result = await store.AddAsync(draft, ct).ConfigureAwait(false);
        if (result.Fault is { } fault)
        {
            return Refuse(fault);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/traffic-rules/{result.Record!.Id}", result.Record);
    }

    private static async Task<IResult> ChangeAsync(long id, TrafficRuleRequest request, TrafficRuleStore store, RouteApplier applier, CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Results.NotFound(new Failure("unknown-traffic-rule", "the traffic rule does not exist"));
        }

        if (Invalid(request) is { } invalid)
        {
            return invalid;
        }

        var draft = Draft(request, held);
        if (draft.Targets.Count == 0 && held.Targets.Count > 0)
        {
            return Results.BadRequest(new Failure("empty-addresses", "specify addresses or domains"));
        }

        var result = await store.ChangeAsync(id, draft, ct).ConfigureAwait(false);
        if (result.Fault is { } fault)
        {
            return Refuse(fault);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);
        return Results.Ok(result.Record);
    }

    private static async Task<IResult> RemoveAsync(long id, TrafficRuleStore store, RouteApplier applier, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (result.Fault is { } fault)
        {
            return Refuse(fault);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    public static TrafficRule Draft(TrafficRuleRequest request, TrafficRule? held = null) => new()
    {
        Name = (request.Name ?? held?.Name ?? string.Empty).Trim(),
        Targets = Clean(request.Targets ?? held?.Targets ?? []),
        DnsMode = request.DnsMode?.Trim() ?? held?.DnsMode
            ?? (request.Dns is { Count: > 0 } || request.DnsMembers is { Count: > 0 } ? TrafficDnsMode.Custom : TrafficDnsMode.Default),
        Dns = Clean(request.Dns ?? held?.Dns ?? []),
        DnsMembers = (request.DnsMembers ?? held?.DnsMembers ?? new Dictionary<string, IReadOnlyList<string>>())
            .ToDictionary(pair => pair.Key, pair => Clean(pair.Value), StringComparer.Ordinal),
    };

    private static IResult? Invalid(TrafficRuleRequest request) =>
        request.Targets?.Any(value => value is null || string.IsNullOrWhiteSpace(value)) == true
        || request.Dns?.Any(value => value is null) == true
        || request.DnsMembers?.Any(pair => pair.Value is null || pair.Value.Any(value => value is null)) == true
            ? Results.BadRequest(new Failure("invalid-addresses", "address and DNS lists contain an invalid item"))
            : null;

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> values) =>
        [.. values.SelectMany(value => value.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    private static IResult Refuse(RouteFault fault) => Results.Json(new Failure(fault.Code, fault.Message), statusCode: fault.Code switch
    {
        "unknown-traffic-rule" => StatusCodes.Status404NotFound,
        "name-taken" or "traffic-rule-in-use" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    });
}

public sealed record TrafficRuleRequest(
    string? Name,
    IReadOnlyList<string>? Targets,
    IReadOnlyList<string>? Dns,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? DnsMembers = null,
    string? DnsMode = null);
