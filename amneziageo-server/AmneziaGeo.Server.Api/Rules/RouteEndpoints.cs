using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// The routes the traffic rules are managed through.
/// </summary>
public static class RouteEndpoints
{
    /// <summary>
    /// Maps the rule management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapRules(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/rules").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/{id:long}", FindAsync);
        reading.MapGet("/basic", BasicAsync);
        reading.MapPost("/test", TestAsync);

        var writing = routes.MapGroup("/api/rules").RequireScope(Scopes.ManageRouting);
        writing.MapGet("/draft", Draft);
        writing.MapGet("/ruleset", RulesetAsync);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapPost("/{id:long}/move", MoveAsync);
        writing.MapPost("/apply", ApplyAsync);
        writing.MapPut("/basic", ChangeBasicAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(RouteStore store, RouteApplier applier, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var plan = await applier.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(rule => RouteAnswers.Rule(rule, Leg(plan, rule.Id))).ToArray());
    }

    private static async Task<IResult> FindAsync(long id, RouteStore store, RouteApplier applier, CancellationToken ct)
    {
        var found = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (found is null)
        {
            return Missing(id);
        }

        var plan = await applier.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(RouteAnswers.Rule(found, Leg(plan, id)));
    }

    private static async Task<IResult> BasicAsync(RouteStore store, RouteApplier applier, CancellationToken ct)
    {
        var basic = await store.ReadBasicAsync(ct).ConfigureAwait(false);
        var plan = await applier.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(RouteAnswers.Basic(basic, plan));
    }

    private static async Task<IResult> ChangeBasicAsync(
        RouteBasicRequest request,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var held = await store.ReadBasicAsync(ct).ConfigureAwait(false);
        var fault = await store.SaveBasicAsync(RouteAnswers.Basic(request, held), ct).ConfigureAwait(false);
        if (fault is not null)
        {
            return Refuse(StatusCodes.Status400BadRequest, fault.Code, fault.Message);
        }

        var plan = await applier.SettleAsync(ct).ConfigureAwait(false);
        var saved = await store.ReadBasicAsync(ct).ConfigureAwait(false);

        return Results.Ok(RouteAnswers.Basic(saved, plan));
    }

    private static async Task<IResult> TestAsync(RouteTestRequest request, RouteTester tester, CancellationToken ct)
    {
        var (fault, answer) = await tester.TestAsync(request, ct).ConfigureAwait(false);

        return fault is null
            ? Results.Ok(answer)
            : Refuse(StatusCodes.Status400BadRequest, fault.Code, fault.Message);
    }

    private static IResult Draft(string? name) =>
        Results.Ok(RouteAnswers.Rule(
            RouteDefaults.Fresh(string.IsNullOrWhiteSpace(name) ? "rule" : name.Trim()),
            null));

    private static async Task<IResult> RulesetAsync(RouteApplier applier, CancellationToken ct)
    {
        var plan = await applier.BuildAsync(ct).ConfigureAwait(false);

        return Results.Ok(new RulesetResponse(RouteRuleset.Text(plan)));
    }

    private static async Task<IResult> AddAsync(
        RouteRequest request,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var result = await store.AddAsync(RouteAnswers.Draft(request), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var plan = await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.Created(
            $"/api/rules/{result.Record!.Id}",
            RouteAnswers.Rule(result.Record, Leg(plan, result.Record.Id)));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        RouteRequest request,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        if (held is null)
        {
            return Missing(id);
        }

        var result = await store.ChangeAsync(id, RouteAnswers.Draft(request, held), ct).ConfigureAwait(false);

        return await AnswerAsync(result, applier, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        RouteSwitchRequest request,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var result = await store.SwitchAsync(id, request.On, ct).ConfigureAwait(false);

        return await AnswerAsync(result, applier, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> MoveAsync(
        long id,
        RouteMoveRequest request,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var result = request.To is { } place
            ? await store.PlaceAsync(id, place, ct).ConfigureAwait(false)
            : await store.MoveAsync(id, request.Up, ct).ConfigureAwait(false);

        return await AnswerAsync(result, applier, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        RouteStore store,
        RouteApplier applier,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> ApplyAsync(RouteStore store, RouteApplier applier, CancellationToken ct)
    {
        try
        {
            var plan = await applier.ApplyAsync(ct).ConfigureAwait(false);
            var found = await store.ListAsync(ct).ConfigureAwait(false);

            return Results.Ok(found.Select(rule => RouteAnswers.Rule(rule, Leg(plan, rule.Id))).ToArray());
        }
        catch (HostNetworkException ex)
        {
            return Refuse(StatusCodes.Status409Conflict, "host-refused", ex.Message);
        }
    }

    private static async Task<IResult> AnswerAsync(RouteResult result, RouteApplier applier, CancellationToken ct)
    {
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var plan = await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(RouteAnswers.Rule(result.Record!, Leg(plan, result.Record!.Id)));
    }

    private static RouteLeg? Leg(RoutePlan plan, long id) => plan.Legs.FirstOrDefault(leg => leg.Rule.Id == id);

    private static IResult Missing(long id) =>
        Refuse(StatusCodes.Status404NotFound, "unknown-rule", $"there is no rule under the number {id}");

    private static IResult Explain(RouteResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(RouteOutcome outcome) => outcome switch
    {
        RouteOutcome.Unknown => StatusCodes.Status404NotFound,
        RouteOutcome.NameTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
