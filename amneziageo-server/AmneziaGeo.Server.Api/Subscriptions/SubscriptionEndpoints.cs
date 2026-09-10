using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The routes the settings of the subscriptions are managed through.
/// </summary>
public static class SubscriptionEndpoints
{
    /// <summary>
    /// Maps the routes of the settings of the subscriptions.
    /// </summary>
    public static IEndpointRouteBuilder MapSubscriptions(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/subscription").RequireScope(Scopes.ManageAccess);
        group.MapGet("/", ReadAsync);
        group.MapPut("/", SaveAsync);

        return routes;
    }

    private static async Task<IResult> ReadAsync(
        SubscriptionStore store,
        SubscriptionState state,
        WebOptions options,
        CancellationToken ct)
    {
        var settings = await store.ReadAsync(ct).ConfigureAwait(false);

        return Results.Ok(SubscriptionAnswers.Settings(settings, state.Fault, options));
    }

    private static async Task<IResult> SaveAsync(
        SubscriptionRequest request,
        SubscriptionStore store,
        SubscriptionServer server,
        SubscriptionState state,
        PanelSettings panel,
        WebOptions options,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var draft = SubscriptionAnswers.Draft(request);
        var logger = loggers.CreateLogger(typeof(SubscriptionEndpoints));
        var fault = SubscriptionRules.Check(draft, panel)
            ?? CertificateFiles.Check(draft.Certificate, draft.CertificateKey, logger);
        if (fault is not null)
        {
            return Refuse(fault.Code, fault.Message);
        }

        var refused = await server.ApplyAsync(draft, ct).ConfigureAwait(false);
        if (refused.Length > 0)
        {
            return Refuse(refused, $"the host did not serve the subscriptions on port {draft.Port}");
        }

        var saved = await store.SaveAsync(draft, ct).ConfigureAwait(false);

        return Results.Ok(SubscriptionAnswers.Settings(saved, state.Fault, options));
    }

    private static IResult Refuse(string code, string message) =>
        Results.Json(new Failure(code, message), statusCode: StatusCodes.Status400BadRequest);
}
