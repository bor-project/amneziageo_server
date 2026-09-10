using System.Runtime.InteropServices;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Diagnostics;

/// <summary>
/// The versions of what the panel runs on.
/// </summary>
public sealed record DiagnosticsResponse(string Runtime, string Distribution, string Kernel, string Wstunnel);

/// <summary>
/// The routes the panel reports on itself through.
/// </summary>
public static class DiagnosticsEndpoints
{
    /// <summary>
    /// Maps the routes of the diagnostics.
    /// </summary>
    public static IEndpointRouteBuilder MapDiagnostics(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var reading = routes.MapGroup("/api/diagnostics").RequireScope(Scopes.ManageAccess);
        reading.MapGet("/", ReadAsync);
        reading.MapGet("/log", (PanelJournal journal) => Results.Ok(journal.Latest()));

        return routes;
    }

    private static async Task<IResult> ReadAsync(CancellationToken ct)
    {
        var wstunnel = await HostVersions.WstunnelAsync(ct).ConfigureAwait(false);

        return Results.Ok(new DiagnosticsResponse(
            RuntimeInformation.FrameworkDescription,
            HostVersions.Distribution(),
            HostVersions.Kernel(),
            wstunnel));
    }
}
