using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Backup;

/// <summary>
/// The route a copy of the server database is taken through.
/// </summary>
public static class BackupEndpoints
{
    /// <summary>
    /// Maps the route of the copy.
    /// </summary>
    public static IEndpointRouteBuilder MapBackup(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/backup").RequireScope(Scopes.ReadBackup).MapGet("/", TakeAsync);

        return routes;
    }

    private static async Task<IResult> TakeAsync(HttpContext context, DatabaseBackup backup, IAuditLog audit, CancellationToken ct)
    {
        if (context.Caller() is not { } caller)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        await audit.WriteAsync(now, caller.Id, caller.Scheme, "backup.take", null, null, context.Address(), ct).ConfigureAwait(false);

        var file = await backup.CopyAsync(ct).ConfigureAwait(false);
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        return Results.File(stream, "application/vnd.sqlite3", FileName(context.Request.Host.Host, now));
    }

    // The name of the copy: the address of the panel and the time it was taken.
    private static string FileName(string host, DateTimeOffset now)
    {
        var name = new string(host.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-').ToArray());

        return name.Length > 0 ? $"amneziageo-{name}-{now:yyyyMMdd-HHmmss}.db" : $"amneziageo-{now:yyyyMMdd-HHmmss}.db";
    }
}
