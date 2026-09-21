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
        writing.MapPost("/apply", Apply);

        return routes;
    }

    private static async Task<IResult> CheckAsync(UpdateCenter center, CancellationToken ct) =>
        Results.Ok(await center.CheckAsync(ct).ConfigureAwait(false));

    private static IResult Apply(UpdateApplyRequest request, UpdateCenter center, ILoggerFactory loggers)
    {
        var version = request.Version?.Trim() ?? string.Empty;
        var refusal = center.Apply(version);
        if (refusal is not null)
        {
            var status = refusal.Error == "update-busy" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;

            return Results.Json(refusal, statusCode: status);
        }

        loggers.CreateLogger(typeof(UpdateEndpoints)).LogInformation("the panel was told to move to {Version}", version);

        return Results.Accepted(value: center.Status());
    }
}
