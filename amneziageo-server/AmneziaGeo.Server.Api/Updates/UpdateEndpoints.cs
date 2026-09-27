using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// The routes the releases of the panel are looked for and put on through.
/// </summary>
public static class UpdateEndpoints
{
    /// <summary>
    /// Registers the look for releases, its schedule and the tools that put a release on.
    /// </summary>
    public static IServiceCollection AddUpdates(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new UpdateOptions();
        configuration.GetSection("Update").Bind(options);

        services.AddSingleton(options);
        services.AddHttpClient(UpdateCenter.Client, http =>
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AmneziaGeo-Server");
            http.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<UpdateCenter>();
        services.AddHostedService<UpdateSchedule>();

        return services;
    }

    /// <summary>
    /// Maps the routes of the releases.
    /// </summary>
    public static IEndpointRouteBuilder MapUpdates(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/update").RequireScope(Scopes.ReadState).MapGet("/", (UpdateCenter center) => Results.Ok(center.Status()));

        var writing = routes.MapGroup("/api/update").RequireScope(Scopes.ManageUpdates);
        writing.MapPost("/check", CheckAsync);
        writing.MapPost("/apply", ApplyAsync);

        return routes;
    }

    private static async Task<IResult> CheckAsync(UpdateCenter center, CancellationToken ct) =>
        Results.Ok(await center.CheckAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> ApplyAsync(
        UpdateApplyRequest request,
        UpdateCenter center,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var version = request.Version?.Trim() ?? string.Empty;
        await center.RefreshAsync(ct).ConfigureAwait(false);
        var refusal = center.Apply(version);
        if (refusal is not null)
        {
            var status = refusal.Error == "update-busy" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;

            return Results.Json(refusal, statusCode: status);
        }

        var moving = center.Status();
        loggers.CreateLogger(typeof(UpdateEndpoints))
            .LogInformation("the panel was told to move to {Version} and moves to {Newest}", version, moving.Latest?.Version);

        return Results.Accepted(value: moving);
    }
}
