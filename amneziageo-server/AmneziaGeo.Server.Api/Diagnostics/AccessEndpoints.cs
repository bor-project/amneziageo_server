using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Access;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// What the connection log is set to and what it holds.
/// </summary>
/// <param name="IsEnabled">Whether the new connections of the clients go to the log.</param>
/// <param name="Days">How many days a record is kept.</param>
/// <param name="IsRunning">Whether the connections go to the log right now.</param>
/// <param name="Fault">Why the log does not run although it is turned on, or empty.</param>
/// <param name="Lost">How many records were lost on the way to the log since the panel started.</param>
/// <param name="Records">How many records the log holds.</param>
/// <param name="Oldest">The earliest record, or null.</param>
/// <param name="Newest">The latest record, or null.</param>
/// <param name="Bytes">The size of the files of the log.</param>
/// <param name="Names">Whether the resolver of the panel runs, which the names of the records come from.</param>
public sealed record AccessResponse(
    bool IsEnabled,
    int Days,
    bool IsRunning,
    string Fault,
    long Lost,
    long Records,
    DateTimeOffset? Oldest,
    DateTimeOffset? Newest,
    long Bytes,
    bool Names);

/// <summary>
/// The settings of the connection log a caller sends; what it leaves out stays as it is.
/// </summary>
/// <param name="IsEnabled">Whether the new connections of the clients go to the log.</param>
/// <param name="Days">How many days a record is kept.</param>
public sealed record AccessRequest(bool? IsEnabled, int? Days);

/// <summary>
/// The routes the connection log is read and set through.
/// </summary>
public static class AccessEndpoints
{
    private static readonly TimeSpan Span = TimeSpan.FromHours(1);

    /// <summary>
    /// Maps the routes of the connection log.
    /// </summary>
    public static IEndpointRouteBuilder MapAccess(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var log = routes.MapGroup("/api/access").RequireScope(Scopes.ManageAccess);
        log.MapGet("/", ReadAsync);
        log.MapPut("/", SaveAsync);
        log.MapGet("/records", ListAsync);
        log.MapGet("/summary", SummaryAsync);
        log.MapDelete("/records", ClearAsync);

        return routes;
    }

    private static async Task<IResult> ReadAsync(AccessHost host, AccessRecords records, DnsState resolver, CancellationToken ct) =>
        Results.Ok(await AnswerAsync(host, records, resolver, ct).ConfigureAwait(false));

    private static async Task<IResult> SaveAsync(
        AccessRequest request,
        AccessStore store,
        AccessHost host,
        AccessRecords records,
        DnsState resolver,
        CancellationToken ct)
    {
        var held = await store.ReadAsync(ct).ConfigureAwait(false);
        var settings = held with
        {
            IsEnabled = request.IsEnabled ?? held.IsEnabled,
            Days = request.Days ?? held.Days,
        };

        if (settings.Check() is { } broken)
        {
            return Results.Json(new Failure("bad-days", broken), statusCode: StatusCodes.Status400BadRequest);
        }

        await store.SaveAsync(settings, ct).ConfigureAwait(false);
        await host.TakeAsync(settings, ct).ConfigureAwait(false);

        return Results.Ok(await AnswerAsync(host, records, resolver, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ListAsync(
        AccessRecords records,
        TimeProvider time,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? client,
        string? verdict,
        string? way,
        string? path,
        string? outcome,
        string? search,
        long? before,
        int? limit,
        CancellationToken ct)
    {
        var query = Query(time, from, to, client, verdict, way, path, outcome, search) with
        {
            Before = before,
            Limit = limit ?? AccessDefaults.Page,
        };

        return Results.Ok(await Task.Run(() => records.List(query), ct).ConfigureAwait(false));
    }

    private static async Task<IResult> SummaryAsync(
        AccessRecords records,
        TimeProvider time,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? client,
        string? verdict,
        string? way,
        string? path,
        string? outcome,
        string? search,
        string? by,
        CancellationToken ct)
    {
        var grouping = by ?? AccessGrouping.Domain;
        if (!AccessGrouping.All.Contains(grouping))
        {
            var failure = new Failure("bad-grouping", $"the records are put together by {string.Join(", ", AccessGrouping.All)}");

            return Results.Json(failure, statusCode: StatusCodes.Status400BadRequest);
        }

        var query = Query(time, from, to, client, verdict, way, path, outcome, search);

        return Results.Ok(await Task.Run(() => records.Summary(query, grouping), ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ClearAsync(AccessHost host, CancellationToken ct)
    {
        await host.ClearAsync(ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static AccessQuery Query(
        TimeProvider time,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? client,
        string? verdict,
        string? way,
        string? path,
        string? outcome,
        string? search)
    {
        var end = to ?? time.GetUtcNow();

        return new AccessQuery
        {
            From = from ?? end - Span,
            To = end,
            Client = client?.Trim() ?? string.Empty,
            Verdict = verdict?.Trim() ?? string.Empty,
            Way = way?.Trim() ?? string.Empty,
            Path = path?.Trim() ?? string.Empty,
            Outcome = outcome?.Trim() ?? string.Empty,
            Search = search?.Trim() ?? string.Empty,
        };
    }

    private static async Task<AccessResponse> AnswerAsync(
        AccessHost host,
        AccessRecords records,
        DnsState resolver,
        CancellationToken ct)
    {
        var stock = await Task.Run(records.Stock, ct).ConfigureAwait(false);
        var settings = host.Settings;

        return new AccessResponse(
            settings.IsEnabled,
            settings.Days,
            host.IsRunning,
            host.Fault ?? string.Empty,
            host.Lost,
            stock.Records,
            stock.Oldest,
            stock.Newest,
            stock.Bytes,
            resolver.IsRunning);
    }
}
