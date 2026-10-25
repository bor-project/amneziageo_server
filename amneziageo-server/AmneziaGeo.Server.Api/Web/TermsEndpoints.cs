using System.Globalization;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The routes the terms of use of the panel are read and accepted through.
/// </summary>
public static class TermsEndpoints
{
    /// <summary>
    /// The version of the terms of use and of the privacy notes the panel asks to accept.
    /// </summary>
    public const int Version = 2;

    /// <summary>
    /// Maps the routes of the terms of use.
    /// </summary>
    public static IEndpointRouteBuilder MapTerms(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/terms");
        group.MapGet("/", ReadAsync).RequireCaller();
        group.MapPost("/", AcceptAsync).RequireScope(Scopes.ManageAccess);

        return routes;
    }

    /// <summary>
    /// Tells the version the panel asks for, whether it is accepted, and the latest acceptance.
    /// </summary>
    public static TermsState State(TermsAcceptance? latest) =>
        new(Version, latest is not null && latest.Version >= Version, latest);

    private static async Task<IResult> ReadAsync(TermsStore store, CancellationToken ct) =>
        Results.Ok(State(await store.LatestAsync(ct).ConfigureAwait(false)));

    private static async Task<IResult> AcceptAsync(
        TermsRequest request,
        HttpContext context,
        TermsStore store,
        IAuditLog audit,
        CancellationToken ct)
    {
        if (context.Caller() is not { } caller)
        {
            return Results.Unauthorized();
        }

        if (request.Version != Version)
        {
            return Results.Json(
                new Failure("terms-version", $"the panel asks to accept version {Version}"),
                statusCode: StatusCodes.Status409Conflict);
        }

        var accepted = await store.AcceptAsync(Version, caller.Id, caller.Name, ct).ConfigureAwait(false);
        await audit
            .WriteAsync(accepted.AcceptedUtc, caller.Id, caller.Scheme, "terms.accept", Version.ToString(CultureInfo.InvariantCulture), null, context.Address(), ct)
            .ConfigureAwait(false);

        return Results.Ok(State(accepted));
    }
}

/// <summary>
/// The version a caller accepts.
/// </summary>
public sealed record TermsRequest(int Version);

/// <summary>
/// The version the panel asks for, whether it is accepted, and the latest acceptance.
/// </summary>
public sealed record TermsState(int Version, bool Accepted, TermsAcceptance? Last);
