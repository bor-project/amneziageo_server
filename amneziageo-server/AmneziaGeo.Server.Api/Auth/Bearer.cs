using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// Reads the bearer token of a request and remembers the caller behind it.
/// </summary>
public sealed class BearerMiddleware
{
    private const string Prefix = "Bearer ";

    private readonly RequestDelegate _next;

    private readonly ITokenIssuer _issuer;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public BearerMiddleware(RequestDelegate next, ITokenIssuer issuer, TimeProvider time)
    {
        _next = next;
        _issuer = issuer;
        _time = time;
    }

    /// <summary>
    /// Puts the caller of a request into its items.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var principal = _issuer.Read(header[Prefix.Length..].Trim(), _time.GetUtcNow());
            if (principal is not null)
            {
                context.Items[Bearer.Item] = principal;
            }
        }

        await _next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// The caller of a request and the rights an endpoint asks of it.
/// </summary>
public static class Bearer
{
    internal const string Item = "amneziageo.caller";

    /// <summary>
    /// Reads bearer tokens on every request.
    /// </summary>
    public static IApplicationBuilder UseBearer(this IApplicationBuilder app) =>
        app.UseMiddleware<BearerMiddleware>();

    /// <summary>
    /// Returns the caller of a request, or null when it carries no valid token.
    /// </summary>
    public static Principal? Caller(this HttpContext context) =>
        context.Items.TryGetValue(Item, out var found) ? found as Principal : null;

    /// <summary>
    /// Refuses a request that carries no valid token.
    /// </summary>
    public static TBuilder RequireCaller<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            context.HttpContext.Caller() is null
                ? Unauthorized()
                : await next(context).ConfigureAwait(false));

    /// <summary>
    /// Refuses a request whose caller does not hold a right.
    /// </summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var caller = context.HttpContext.Caller();
            if (caller is null)
            {
                return Unauthorized();
            }

            return caller.Holds(scope)
                ? await next(context).ConfigureAwait(false)
                : Results.Json(new Failure("forbidden", "the account does not hold " + scope), statusCode: StatusCodes.Status403Forbidden);
        });

    private static IResult Unauthorized() =>
        Results.Json(new Failure("unauthorized", "the request carries no valid access token"), statusCode: StatusCodes.Status401Unauthorized);
}

/// <summary>
/// A refusal as the interface reads it.
/// </summary>
public sealed record Failure(string Error, string Message);
