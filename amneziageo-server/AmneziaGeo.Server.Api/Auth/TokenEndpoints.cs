using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// The routes long lived tokens are minted, listed and revoked through.
/// </summary>
public static class TokenEndpoints
{
    /// <summary>
    /// Maps the token management routes.
    /// </summary>
    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tokens").RequireScope(Scopes.ManageAccess);

        group.MapGet("/", ListAsync);
        group.MapPost("/", MintAsync);
        group.MapDelete("/{id:long}", RevokeAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(ApiTokenManager tokens, TimeProvider time, CancellationToken ct)
    {
        var found = await tokens.ListAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();

        return Results.Ok(found.Select(view => TokenAnswers.Token(view, now)).ToArray());
    }

    private static async Task<IResult> MintAsync(
        TokenRequest request,
        HttpContext http,
        ApiTokenManager tokens,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return Refuse(StatusCodes.Status400BadRequest, "incomplete", "a token needs the role it acts by");
        }

        var result = await tokens
            .MintAsync(request.Name, request.Role, request.Days, http.Caller(), http.Address(), ct)
            .ConfigureAwait(false);

        if (!result.IsOk)
        {
            return Explain(result);
        }

        var minted = result.Minted!;
        var answer = new MintedResponse(TokenAnswers.Token(minted.Token, time.GetUtcNow()), minted.Secret);

        return Results.Created($"/api/tokens/{minted.Token.Id}", answer);
    }

    private static async Task<IResult> RevokeAsync(long id, HttpContext http, ApiTokenManager tokens, CancellationToken ct)
    {
        var result = await tokens.RevokeAsync(id, http.Caller(), http.Address(), ct).ConfigureAwait(false);

        return result.IsOk ? Results.NoContent() : Explain(result);
    }

    private static IResult Explain(ApiTokenResult result) => result.Outcome switch
    {
        ApiTokenOutcome.BadName => Refuse(StatusCodes.Status400BadRequest, "bad-token-name", result.Message),
        ApiTokenOutcome.BadLifetime => Refuse(StatusCodes.Status400BadRequest, "bad-token-lifetime", result.Message),
        ApiTokenOutcome.UnknownRole => Refuse(StatusCodes.Status400BadRequest, "unknown-role", result.Message),
        ApiTokenOutcome.NameTaken => Refuse(StatusCodes.Status409Conflict, "token-name-taken", result.Message),
        ApiTokenOutcome.Unknown => Refuse(StatusCodes.Status404NotFound, "unknown-token", result.Message),
        _ => Refuse(StatusCodes.Status400BadRequest, "refused", result.Message),
    };

    private static IResult Refuse(int status, string error, string message) =>
        Results.Json(new Failure(error, message), statusCode: status);
}
