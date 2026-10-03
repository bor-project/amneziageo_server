using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Http.Features;

namespace AmneziaGeo.Server.Api.Backup;

/// <summary>
/// What a backup the panel took in left: the panel starts over on it.
/// </summary>
/// <param name="Restarting">Whether the panel starts over on the backup.</param>
public sealed record BackupRestoreAnswer(bool Restarting);

/// <summary>
/// The routes a copy of the server database is taken through and put back through.
/// </summary>
public static class BackupEndpoints
{
    private const long Largest = 1L << 30;

    /// <summary>
    /// Maps the routes of the copy.
    /// </summary>
    public static IEndpointRouteBuilder MapBackup(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGroup("/api/backup").RequireScope(Scopes.ReadBackup).MapGet("/", TakeAsync);
        routes.MapGroup("/api/backup").RequireScope(Scopes.ManageAccess).MapPost("/restore", RestoreAsync);

        return routes;
    }

    /// <summary>
    /// Puts a backup the panel took in and left beside the database in its place before anything opens the database,
    /// the database it replaces set aside in the backup directory.
    /// </summary>
    public static WebApplicationBuilder RestoreDatabase(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var path = builder.Configuration["Database:Path"] is { Length: > 0 } set ? set : ServerDatabase.DefaultPath();
        if (DatabaseRestore.Swap(path, DateTimeOffset.UtcNow) is { } aside)
        {
            Console.WriteLine($"the database is put back from a backup, the database it replaced lies in {aside}");
        }

        return builder;
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

    // Takes a backup in, checks that the panel comes up on it on this host, leaves it beside the database and starts
    // the panel over on it.
    private static async Task<IResult> RestoreAsync(
        HttpContext context,
        DatabaseBackup backup,
        WebOptions options,
        IAuditLog audit,
        IHostApplicationLifetime life,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (context.Caller() is not { } caller)
        {
            return Results.Unauthorized();
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = Largest;
        }

        string file;
        try
        {
            file = await backup.ReceiveAsync(context.Request.Body, ct).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Refuse("backup-large", "the backup is larger than the 1 GiB the panel takes", ex.StatusCode);
        }

        var staged = false;
        try
        {
            if (Refusal(file, options) is { } refused)
            {
                return refused;
            }

            await audit.WriteAsync(DateTimeOffset.UtcNow, caller.Id, caller.Scheme, "backup.restore", null, null, context.Address(), ct)
                .ConfigureAwait(false);
            backup.Stage(file);
            staged = true;
        }
        finally
        {
            if (!staged)
            {
                DatabaseRestore.Drop(file);
            }
        }

        loggers.CreateLogger(typeof(BackupEndpoints))
            .LogInformation("{Name} put a backup in place of the database, the panel starts over on it", caller.Name);
        PanelEndpoints.StartOver(life);

        return Results.Accepted(value: new BackupRestoreAnswer(true));
    }

    // Returns the refusal of a backup that is empty, is no sound database of the panel this release takes, or carries
    // settings the panel would not come up under on this host; null for a backup it takes.
    private static IResult? Refusal(string file, WebOptions options)
    {
        if (new FileInfo(file).Length == 0)
        {
            return Refuse("backup-empty", "the backup is empty");
        }

        var verdict = DatabaseCheck.Judge(file, DatabaseCheck.Newest());
        if (!verdict.IsSound)
        {
            return Refuse(verdict.Code, verdict.Message);
        }

        return PanelStore.Held(file) is { } held && Listening.Fault(options, held) is { } fault
            ? Refuse(
                "backup-elsewhere",
                $"{fault}; the console of the host fits such a backup to it: amneziageo-server restore <file>")
            : null;
    }

    // Answers a refusal with its code, 400 unless told otherwise.
    private static IResult Refuse(string code, string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new Failure(code, message), statusCode: status);

    // The name of the copy: the address of the panel and the time it was taken.
    private static string FileName(string host, DateTimeOffset now)
    {
        var name = new string(host.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-').ToArray());

        return name.Length > 0 ? $"amneziageo-{name}-{now:yyyyMMdd-HHmmss}.db" : $"amneziageo-{now:yyyyMMdd-HHmmss}.db";
    }
}
