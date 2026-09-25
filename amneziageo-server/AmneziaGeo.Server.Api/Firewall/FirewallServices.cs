using System.ComponentModel;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Firewall;

/// <summary>
/// Whether the firewall of the host lets a port in from outside.
/// </summary>
/// <param name="Port">The port asked about.</param>
/// <param name="Protocol">Whether the port takes tcp or udp.</param>
/// <param name="State">open, closed or unknown.</param>
/// <param name="Engine">What the panel holds its ports open with.</param>
public sealed record FirewallPortAnswer(int Port, string Protocol, string State, string Engine);

/// <summary>
/// Registers what the ports of the panel are held open with.
/// </summary>
public static class FirewallServices
{
    /// <summary>
    /// Adds the firewall of the host and the applier the panel opens its ports through.
    /// </summary>
    public static IServiceCollection AddFirewall(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => new FirewallHost(
            provider.GetRequiredService<IHostCommands>(),
            provider.GetRequiredService<IHostNetwork>()));
        services.AddScoped<FirewallApplier>();

        return services;
    }

    /// <summary>
    /// Maps the route that tells whether the firewall of the host lets a port in.
    /// </summary>
    public static IEndpointRouteBuilder MapFirewall(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/firewall").RequireScope(Scopes.ReadState).MapGet("/port", PortAsync);

        return routes;
    }

    /// <summary>
    /// Opens the ports the panel keeps open as the server comes up.
    /// </summary>
    public static WebApplication SettleFirewall(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FirewallApplier>()
            .SettleAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return app;
    }

    private static async Task<IResult> PortAsync(
        int port,
        string? protocol,
        FirewallHost host,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var kind = string.Equals(protocol, FirewallPlan.Udp, StringComparison.OrdinalIgnoreCase) ? FirewallPlan.Udp : FirewallPlan.Tcp;
        if (port is <= 0 or > 65535)
        {
            return Results.Json(new Failure("port-invalid", "the port lies outside 1-65535"), statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var state = await host.StateAsync(kind, port, ct).ConfigureAwait(false);

            return Results.Ok(new FirewallPortAnswer(port, kind, state, host.Engine));
        }
        catch (Exception ex) when (ex is HostNetworkException or IOException or UnauthorizedAccessException
            or InvalidOperationException or Win32Exception)
        {
            loggers.CreateLogger(typeof(FirewallServices))
                .LogWarning(ex, "the firewall of the host did not tell whether port {Port} is open", port);

            return Results.Ok(new FirewallPortAnswer(port, kind, PortState.Unknown, host.Engine));
        }
    }
}
