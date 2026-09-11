using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// The routes the roles of the panel are managed through.
/// </summary>
public static class RoleEndpoints
{
    /// <summary>
    /// Maps the role management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapRoles(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/roles").RequireScope(Scopes.ManageAccess);

        group.MapGet("/", ListAsync);
        group.MapPost("/", AddAsync);
        group.MapPatch("/{name}", PatchAsync);
        group.MapDelete("/{name}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(RoleCatalog roles, CancellationToken ct)
    {
        var found = await roles.ListAsync(ct).ConfigureAwait(false);

        return Results.Ok(new RoleListResponse([.. found.Select(RoleAnswers.Role)], [.. Scopes.All]));
    }

    private static async Task<IResult> AddAsync(RoleCreateRequest request, RoleCatalog roles, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a new role needs a name");
        }

        var result = await roles
            .AddAsync(request.Name.Trim(), request.Title, request.Scopes ?? [], ct)
            .ConfigureAwait(false);

        return result.IsOk
            ? Results.Created($"/api/roles/{result.Record!.Name}", await AnswerAsync(result.Record.Name!, roles, ct).ConfigureAwait(false))
            : Explain(result);
    }

    private static async Task<IResult> PatchAsync(string name, RolePatchRequest request, RoleCatalog roles, CancellationToken ct)
    {
        if (request.Title is null && request.Scopes is null)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "the change names nothing to change");
        }

        var result = await roles.ChangeAsync(name, request.Title, request.Scopes, ct).ConfigureAwait(false);

        return result.IsOk
            ? Results.Ok(await AnswerAsync(result.Record!.Name!, roles, ct).ConfigureAwait(false))
            : Explain(result);
    }

    private static async Task<IResult> RemoveAsync(string name, RoleCatalog roles, CancellationToken ct)
    {
        var result = await roles.RemoveAsync(name, ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static async Task<RoleResponse?> AnswerAsync(string name, RoleCatalog roles, CancellationToken ct)
    {
        var view = await roles.FindAsync(name, ct).ConfigureAwait(false);

        return view is null ? null : RoleAnswers.Role(view);
    }

    private static IResult Explain(RoleResult result) => result.Outcome switch
    {
        RoleOutcome.Unknown => Refuse(StatusCodes.Status404NotFound, "unknown-role", result.Message),
        RoleOutcome.NameTaken => Refuse(StatusCodes.Status409Conflict, "name-taken", result.Message),
        RoleOutcome.BadName => Refuse(StatusCodes.Status400BadRequest, "bad-name", result.Message),
        RoleOutcome.Builtin => Refuse(StatusCodes.Status409Conflict, "builtin-role", result.Message),
        RoleOutcome.InUse => Refuse(StatusCodes.Status409Conflict, "role-in-use", result.Message),
        RoleOutcome.HasTokens => Refuse(StatusCodes.Status409Conflict, "role-has-tokens", result.Message),
        RoleOutcome.UnknownScope => Refuse(StatusCodes.Status400BadRequest, "unknown-scope", result.Message),
        _ => Refuse(StatusCodes.Status400BadRequest, "refused", result.Message),
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
