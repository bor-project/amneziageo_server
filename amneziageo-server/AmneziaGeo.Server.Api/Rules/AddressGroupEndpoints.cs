using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Api.Rules;

/// <summary>
/// Maps address group operations.
/// </summary>
public static class AddressGroupEndpoints
{
    /// <summary>
    /// Maps address groups.
    /// </summary>
    public static IEndpointRouteBuilder MapAddressGroups(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/api/address-groups").RequireScope(Scopes.ReadState).MapGet("/", ListAsync);
        var writing = routes.MapGroup("/api/address-groups").RequireScope(Scopes.ManageRouting);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(AddressGroupStore store, RouteStore rules, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var table = await rules.TableAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(group => new AddressGroupResponse(group.Id, group.Name, group.Targets,
            [.. table.Where(rule => rule.Groups.Contains(group.Id)).Select(rule => rule.Id)])));
    }

    private static async Task<IResult> AddAsync(AddressGroupRequest request, AddressGroupStore store, RouteApplier applier, CancellationToken ct)
    {
        var result = await store.AddAsync(Draft(request), ct).ConfigureAwait(false);

        return await AnswerAsync(result, applier, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> ChangeAsync(long id, AddressGroupRequest request, AddressGroupStore store, RouteApplier applier, CancellationToken ct)
    {
        var held = (await store.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(group => group.Id == id);
        if (held is null)
        {
            return Refuse(new RouteFault("unknown-address-group", "the address group does not exist"));
        }

        var result = await store.ChangeAsync(id, Draft(request, held), ct).ConfigureAwait(false);

        return await AnswerAsync(result, applier, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RemoveAsync(long id, AddressGroupStore store, RouteApplier applier, CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (result.Fault is { } fault)
        {
            return Refuse(fault);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> AnswerAsync(AddressGroupResult result, RouteApplier applier, CancellationToken ct)
    {
        if (result.Fault is { } fault)
        {
            return Refuse(fault);
        }

        await applier.SettleAsync(ct).ConfigureAwait(false);

        return Results.Ok(result.Record);
    }

    private static AddressGroup Draft(AddressGroupRequest request, AddressGroup? held = null) => new()
    {
        Name = request.Name ?? held?.Name ?? string.Empty,
        Targets = request.Targets is null ? held?.Targets ?? [] : Clean(request.Targets),
    };

    private static IReadOnlyList<string> Clean(IReadOnlyList<string> targets) =>
        [.. targets.SelectMany(target => target.Split([',', ' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    private static IResult Refuse(RouteFault fault) =>
        Results.Json(new Failure(fault.Code, fault.Message), statusCode: fault.Code switch
        {
            "unknown-address-group" => StatusCodes.Status404NotFound,
            "address-group-in-use" or "name-taken" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        });
}

public sealed record AddressGroupRequest(string? Name, IReadOnlyList<string>? Targets);

public sealed record AddressGroupResponse(long Id, string Name, IReadOnlyList<string> Targets, IReadOnlyList<long> Rules);
