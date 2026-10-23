using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

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

    private static async Task<IResult> AddAsync(
        RoleCreateRequest request,
        HttpContext http,
        RoleCatalog roles,
        AccountManager accounts,
        HostUsers hosts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a new role needs a name");
        }

        var result = await roles
            .AddAsync(request.Name.Trim(), request.Title, request.Scopes ?? [], ct)
            .ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        var name = result.Record!.Name!;
        if (request.Users is { Length: > 0 } users)
        {
            var actor = http.Caller()!.Id;
            if (await accounts.WhyNotHandAsync(name, users, actor, ct).ConfigureAwait(false) is { } refusal)
            {
                await roles.RemoveAsync(name, ct).ConfigureAwait(false);

                return UserEndpoints.Explain(refusal);
            }

            if (await HandAsync(name, users, actor, roles, accounts, hosts, ct).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }

        return Results.Created($"/api/roles/{name}", await AnswerAsync(name, roles, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> PatchAsync(
        string name,
        RolePatchRequest request,
        HttpContext http,
        RoleCatalog roles,
        AccountManager accounts,
        HostUsers hosts,
        CancellationToken ct)
    {
        if (request.Title is null && request.Scopes is null && request.Users is null)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "the change names nothing to change");
        }

        // Judges the handing by the rights the role carries after the change, before anything is written.
        var actor = http.Caller()!.Id;
        if (request.Users is not null
            && await roles.FindAsync(name, ct).ConfigureAwait(false) is not null
            && await accounts.WhyNotHandAsync(name, request.Users, request.Scopes, actor, ct).ConfigureAwait(false) is { } refusal)
        {
            return UserEndpoints.Explain(refusal);
        }

        var result = await roles.ChangeAsync(name, request.Title, request.Scopes, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        if (request.Users is not null
            && await HandAsync(result.Record!.Name!, request.Users, actor, roles, accounts, hosts, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        return Results.Ok(await AnswerAsync(result.Record!.Name!, roles, ct).ConfigureAwait(false));
    }

    // Hands the role to the accounts and carries the moved users of the host to it, returning the refusal or null.
    private static async Task<IResult?> HandAsync(
        string role,
        IReadOnlyCollection<string> users,
        long actor,
        RoleCatalog roles,
        AccountManager accounts,
        HostUsers hosts,
        CancellationToken ct)
    {
        var handed = await accounts.HandRoleAsync(role, users, actor, ct).ConfigureAwait(false);
        foreach (var user in handed.Moved)
        {
            if (await UserEndpoints.HostAsync(user, accounts, roles, hosts, ct).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }

        return handed.Refusal is null ? null : UserEndpoints.Explain(handed.Refusal);
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
