using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// The routes an account signs in, refreshes and signs out through.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps the login routes.
    /// </summary>
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync).RequireCaller();
        group.MapGet("/me", MeAsync).RequireCaller();
        group.MapPost("/password", PasswordAsync).RequireScope(Scopes.ChangePassword);

        return routes;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        LoginService login,
        AccountManager accounts,
        AuthOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.User) || string.IsNullOrEmpty(request.Password))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a login needs a name and a password");
        }

        var result = await login
            .PasswordAsync(request.User, request.Password, http.Address(), Agent(http), ct)
            .ConfigureAwait(false);

        return await AnswerAsync(result, accounts, options, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        LoginService login,
        AccountManager accounts,
        AuthOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Refresh))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a refresh needs a token");
        }

        var result = await login.RefreshAsync(request.Refresh, ct).ConfigureAwait(false);

        return await AnswerAsync(result, accounts, options, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, LoginService login, CancellationToken ct)
    {
        var caller = http.Caller()!;
        if (caller.SessionId > 0)
        {
            await login.EndAsync(caller.SessionId, ct).ConfigureAwait(false);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, AccountManager accounts, CancellationToken ct)
    {
        var caller = http.Caller()!;
        if (caller.Scheme == AuthScheme.ApiToken)
        {
            return Results.Ok(AuthAnswers.Token(caller));
        }

        var view = await accounts.FindAsync(caller.Name, ct).ConfigureAwait(false);
        if (view is null || !view.Record.IsEnabled)
        {
            return Refuse(StatusCodes.Status401Unauthorized, "unauthorized", "the account is gone or switched off");
        }

        return Results.Ok(AuthAnswers.Account(view, caller.Scheme, caller.Scopes));
    }

    private static async Task<IResult> PasswordAsync(
        PasswordRequest request,
        HttpContext http,
        LoginService login,
        AccountManager accounts,
        AuthOptions options,
        CancellationToken ct)
    {
        var caller = http.Caller()!;
        if (string.IsNullOrEmpty(request.Current) || string.IsNullOrEmpty(request.Next))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a change needs the current password and the new one");
        }

        var view = await accounts.FindAsync(caller.Name, ct).ConfigureAwait(false);
        if (view is null || !view.Record.IsEnabled)
        {
            return Refuse(StatusCodes.Status401Unauthorized, "unauthorized", "the account is gone or switched off");
        }

        var changed = await accounts.ChangePasswordAsync(caller.Id, request.Current, request.Next, ct).ConfigureAwait(false);
        if (!changed.IsOk)
        {
            return changed.Outcome == AccountOutcome.Weak
                ? Refuse(StatusCodes.Status400BadRequest, "weak", changed.Message)
                : Refuse(StatusCodes.Status401Unauthorized, "wrong-password", "the current password does not hold");
        }

        if (caller.SessionId > 0)
        {
            await login.EndAsync(caller.SessionId, ct).ConfigureAwait(false);
        }

        var result = await login
            .PasswordAsync(caller.Name, request.Next, http.Address(), Agent(http), ct)
            .ConfigureAwait(false);

        return await AnswerAsync(result, accounts, options, ct).ConfigureAwait(false);
    }

    private static async Task<IResult> AnswerAsync(
        LoginResult result,
        AccountManager accounts,
        AuthOptions options,
        CancellationToken ct)
    {
        if (!result.IsOk || result.Principal is null)
        {
            return Explain(result.Outcome);
        }

        var view = await accounts.FindAsync(result.Principal.Name, ct).ConfigureAwait(false);

        return view is null
            ? Refuse(StatusCodes.Status401Unauthorized, "unauthorized", "the account is gone")
            : Results.Ok(AuthAnswers.Session(result, view, options));
    }

    private static IResult Explain(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Locked => Refuse(StatusCodes.Status423Locked, "locked", "too many wrong passwords, the account is locked for a while"),
        LoginOutcome.Disabled => Refuse(StatusCodes.Status403Forbidden, "disabled", "the account is switched off"),
        LoginOutcome.NoPassword => Refuse(StatusCodes.Status401Unauthorized, "no-password", "the account carries no password"),
        LoginOutcome.RefreshExpired => Refuse(StatusCodes.Status401Unauthorized, "refresh-expired", "the refresh token ran out"),
        LoginOutcome.RefreshReplayed => Refuse(StatusCodes.Status401Unauthorized, "refresh-replayed", "the refresh token was already spent, the session is closed"),
        LoginOutcome.SessionEnded => Refuse(StatusCodes.Status401Unauthorized, "session-ended", "the session is closed"),
        LoginOutcome.RefreshUnknown => Refuse(StatusCodes.Status401Unauthorized, "refresh-unknown", "the refresh token is not one of ours"),
        _ => Refuse(StatusCodes.Status401Unauthorized, "wrong-credentials", "the name or the password does not hold"),
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);

    private static string? Agent(HttpContext http) =>
        http.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;
}
