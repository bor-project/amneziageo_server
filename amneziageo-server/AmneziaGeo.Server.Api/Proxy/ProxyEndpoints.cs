using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Proxy;

/// <summary>
/// The routes the websocket proxies are managed through.
/// </summary>
public static class ProxyEndpoints
{
    /// <summary>
    /// Maps the routes of the proxies.
    /// </summary>
    public static IEndpointRouteBuilder MapProxies(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/proxies").RequireScope(Scopes.ReadState);
        reading.MapGet("/", ListAsync);
        reading.MapGet("/certificate", CertificateAsync);

        var writing = routes.MapGroup("/api/proxies").RequireScope(Scopes.ManageRouting);
        writing.MapGet("/draft", Draft);
        writing.MapPost("/", AddAsync);
        writing.MapPut("/{id:long}", ChangeAsync);
        writing.MapPost("/{id:long}/switch", SwitchAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(ProxyStore store, ProxyApplier applier, CancellationToken ct)
    {
        var found = await store.ListAsync(ct).ConfigureAwait(false);
        var answers = new List<ProxyResponse>(found.Count);
        foreach (var proxy in found)
        {
            var state = await applier.StateAsync(proxy, ct).ConfigureAwait(false);
            answers.Add(ProxyAnswers.Proxy(proxy, state));
        }

        return Results.Ok(answers);
    }

    private static async Task<IResult> CertificateAsync(ProxyApplier applier, CancellationToken ct) =>
        Results.Ok(await applier.PanelCertificateAsync(ct).ConfigureAwait(false));

    private static IResult Draft(string? name) =>
        Results.Ok(ProxyAnswers.Proxy(
            ProxyDefaults.Fresh(string.IsNullOrWhiteSpace(name) ? ProxyDefaults.FirstName : name.Trim()),
            ProxyState.Down));

    private static async Task<IResult> AddAsync(
        ProxyRequest request,
        ProxyStore store,
        ProxyApplier applier,
        CancellationToken ct)
    {
        var draft = ProxyAnswers.Draft(request);
        if (await ReadyAsync(draft, applier, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        var result = await store.AddAsync(draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Explain(result);
        }

        var state = await applier.ApplyAsync(result.Record, ct).ConfigureAwait(false);

        return Results.Created($"/api/proxies/{result.Record.Id}", ProxyAnswers.Proxy(result.Record, state));
    }

    private static async Task<IResult> ChangeAsync(
        long id,
        ProxyRequest request,
        ProxyStore store,
        ProxyApplier applier,
        CancellationToken ct)
    {
        var draft = ProxyAnswers.Draft(request);
        if (await ReadyAsync(draft, applier, ct).ConfigureAwait(false) is { } missing)
        {
            return missing;
        }

        var held = await store.FindAsync(id, ct).ConfigureAwait(false);
        var result = await store.ChangeAsync(id, draft, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Explain(result);
        }

        if (held is not null && !string.Equals(held.Name, result.Record.Name, StringComparison.Ordinal))
        {
            await applier.WithdrawAsync(held.Name, ct).ConfigureAwait(false);
        }

        var state = await applier.ApplyAsync(result.Record, ct).ConfigureAwait(false);

        return Results.Ok(ProxyAnswers.Proxy(result.Record, state));
    }

    private static async Task<IResult> SwitchAsync(
        long id,
        ProxySwitchRequest request,
        ProxyStore store,
        ProxyApplier applier,
        CancellationToken ct)
    {
        var result = await store.SwitchAsync(id, request.On, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Explain(result);
        }

        var state = await applier.ApplyAsync(result.Record, ct).ConfigureAwait(false);

        return Results.Ok(ProxyAnswers.Proxy(result.Record, state));
    }

    private static async Task<IResult> RemoveAsync(
        long id,
        ProxyStore store,
        ProxyApplier applier,
        CancellationToken ct)
    {
        var result = await store.RemoveAsync(id, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Record is null)
        {
            return Explain(result);
        }

        await applier.WithdrawAsync(result.Record.Name, ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult?> ReadyAsync(ProxyConfig draft, ProxyApplier applier, CancellationToken ct)
    {
        var certificate = await applier.CertificateAsync(draft, ct).ConfigureAwait(false);

        return ProxyRules.CheckReady(draft, certificate.Chain, certificate.Key) is { } missing
            ? Refuse(StatusCodes.Status400BadRequest, missing.Code, missing.Message)
            : null;
    }

    private static IResult Explain(ProxyResult result) => Refuse(Status(result.Outcome), result.Code, result.Message);

    private static int Status(ProxyOutcome outcome) => outcome switch
    {
        ProxyOutcome.Unknown => StatusCodes.Status404NotFound,
        ProxyOutcome.NameTaken or ProxyOutcome.PortTaken => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static IResult Refuse(int status, string code, string message) =>
        Results.Json(new Failure(code, message), statusCode: status);
}
