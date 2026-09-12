using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// The routes the accounts of the panel are managed through.
/// </summary>
public static class UserEndpoints
{
    /// <summary>
    /// Maps the account management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").RequireScope(Scopes.ManageAccess);

        group.MapGet("/", ListAsync);
        group.MapPost("/", AddAsync);
        group.MapPatch("/{name}", PatchAsync);
        group.MapPut("/{name}/password", PasswordAsync);
        group.MapDelete("/{name}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(AccountManager accounts, CancellationToken ct)
    {
        var found = await accounts.ListAsync(ct).ConfigureAwait(false);

        return Results.Ok(found.Select(UserAnswers.User).ToArray());
    }

    private static async Task<IResult> AddAsync(
        UserCreateRequest request,
        AccountManager accounts,
        RoleCatalog roles,
        HostUsers hosts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrEmpty(request.Password))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a new account needs a name and a password");
        }

        var name = request.Name.Trim();
        var result = await accounts
            .AddAsync(
                name,
                request.DisplayName,
                request.Role,
                request.Password,
                request.MustChangePassword,
                request.Host,
                request.PublicKey,
                ct)
            .ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        if (request.Host)
        {
            var carried = await hosts
                .CarryAsync(name, request.Role, request.PublicKey, await NamesAsync(roles, ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
            if (!carried.IsDone)
            {
                await accounts.RemoveAsync(name, 0, ct).ConfigureAwait(false);

                return Refuse(StatusCodes.Status400BadRequest, "host-refused", carried.Message);
            }
        }

        return Results.Created($"/api/users/{result.Record!.Name}", await AnswerAsync(result.Record, accounts).ConfigureAwait(false));
    }

    private static async Task<IResult> PatchAsync(
        string name,
        UserPatchRequest request,
        HttpContext http,
        AccountManager accounts,
        RoleCatalog roles,
        HostUsers hosts,
        CancellationToken ct)
    {
        var actor = http.Caller()!.Id;
        var result = default(AccountResult);

        if (request.Role is { Length: > 0 } role)
        {
            result = await accounts.SetRoleAsync(name, role, actor, ct).ConfigureAwait(false);
            if (!result.IsOk)
            {
                return Explain(result);
            }
        }

        if (request.PublicKey is { Length: > 0 } key)
        {
            result = await accounts.SetHostKeyAsync(name, key, ct).ConfigureAwait(false);
            if (!result.IsOk)
            {
                return Explain(result);
            }
        }

        if (request.Enabled is { } enabled)
        {
            result = await accounts.SetEnabledAsync(name, enabled, actor, ct).ConfigureAwait(false);
            if (!result.IsOk)
            {
                return Explain(result);
            }
        }

        if (result?.Record is { HostUserName: { Length: > 0 } host } record)
        {
            var known = await NamesAsync(roles, ct).ConfigureAwait(false);
            var sync = record.IsEnabled
                ? await hosts.CarryAsync(host, await RoleOfAsync(accounts, record, ct).ConfigureAwait(false), record.HostKey, known, ct).ConfigureAwait(false)
                : await hosts.ShutAsync(host, known, ct).ConfigureAwait(false);
            if (!sync.IsDone)
            {
                return Refuse(StatusCodes.Status400BadRequest, "host-refused", sync.Message);
            }
        }

        if (result is null)
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "the change names nothing to change");
        }

        return Results.Ok(await AnswerAsync(result.Record!, accounts).ConfigureAwait(false));
    }

    private static async Task<IResult> PasswordAsync(
        string name,
        UserPasswordRequest request,
        AccountManager accounts,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Password))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a password change needs a password");
        }

        var result = await accounts
            .SetPasswordAsync(name, request.Password, request.MustChangePassword, ct)
            .ConfigureAwait(false);

        return result.IsOk
            ? Results.Ok(await AnswerAsync(result.Record!, accounts).ConfigureAwait(false))
            : Explain(result);
    }

    private static async Task<IResult> RemoveAsync(
        string name,
        HttpContext http,
        AccountManager accounts,
        RoleCatalog roles,
        HostUsers hosts,
        CancellationToken ct)
    {
        var held = await accounts.FindAsync(name, ct).ConfigureAwait(false);
        var result = await accounts.RemoveAsync(name, http.Caller()!.Id, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            return Explain(result);
        }

        if (held?.Record.HostUserName is { Length: > 0 } host)
        {
            await hosts.ReleaseAsync(host, await NamesAsync(roles, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(RoleCatalog roles, CancellationToken ct) =>
        [.. (await roles.ListAsync(ct).ConfigureAwait(false)).Select(role => role.Record.Name ?? string.Empty)];

    private static async Task<string?> RoleOfAsync(AccountManager accounts, AppUser user, CancellationToken ct) =>
        (await accounts.ViewAsync(user).ConfigureAwait(false)).Role;

    private static async Task<UserResponse> AnswerAsync(AppUser user, AccountManager accounts) =>
        UserAnswers.User(await accounts.ViewAsync(user).ConfigureAwait(false));

    private static IResult Explain(AccountResult result) => result.Outcome switch
    {
        AccountOutcome.Unknown => Refuse(StatusCodes.Status404NotFound, "unknown-user", result.Message),
        AccountOutcome.NameTaken => Refuse(StatusCodes.Status409Conflict, "name-taken", result.Message),
        AccountOutcome.HostName => Refuse(StatusCodes.Status409Conflict, "host-name", result.Message),
        AccountOutcome.BadName => Refuse(StatusCodes.Status400BadRequest, "bad-name", result.Message),
        AccountOutcome.Weak => Refuse(StatusCodes.Status400BadRequest, "weak", result.Message),
        AccountOutcome.UnknownRole => Refuse(StatusCodes.Status400BadRequest, "unknown-role", result.Message),
        AccountOutcome.BadKey => Refuse(StatusCodes.Status400BadRequest, "bad-host-key", result.Message),
        AccountOutcome.LastAdmin => Refuse(StatusCodes.Status409Conflict, "last-admin", result.Message),
        AccountOutcome.Self => Refuse(StatusCodes.Status409Conflict, "self", result.Message),
        _ => Refuse(StatusCodes.Status400BadRequest, "refused", result.Message),
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
