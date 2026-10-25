using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// Answers the requests of the releases under a prefix: the manifests and the files answer everyone, the rest answers
/// whoever holds the token.
/// </summary>
public static class UpdateApi
{
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".json"] = "application/json",
        [".exe"] = "application/vnd.microsoft.portable-executable",
        [".deb"] = "application/vnd.debian.binary-package",
        [".apk"] = "application/vnd.android.package-archive",
        [ReleaseSlots.PatchExtension] = "application/octet-stream",
    };

    /// <summary>
    /// Answers a request that comes under the prefix of the releases; false when it comes under something else.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="prefix">The path the releases come under, a slash or empty for the root.</param>
    /// <param name="store">The store that answers, null while the releases are off.</param>
    /// <param name="allowed">Tells whether a request may change the releases.</param>
    public static async Task<bool> TryAnswerAsync(HttpContext context, string prefix, ReleaseStore? store, Func<HttpContext, bool> allowed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(allowed);

        if (!Under(context.Request.Path, prefix, out var rest))
        {
            return false;
        }

        var result = await RouteAsync(context, rest, store, allowed).ConfigureAwait(false);
        await result.ExecuteAsync(context).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Returns the type a file of a release is served as.
    /// </summary>
    public static string TypeOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Types.TryGetValue(Path.GetExtension(name).ToLowerInvariant(), out var type) ? type : "application/octet-stream";
    }

    private static bool Under(PathString path, string prefix, out string[] rest)
    {
        var lead = "/" + prefix.Trim('/');
        if (lead.Length == 1)
        {
            rest = Segments(path);

            return true;
        }

        if (!path.StartsWithSegments(lead, StringComparison.Ordinal, out var remaining))
        {
            rest = [];

            return false;
        }

        rest = Segments(remaining);

        return true;
    }

    private static string[] Segments(PathString path) => (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static async Task<IResult> RouteAsync(HttpContext context, string[] rest, ReleaseStore? store, Func<HttpContext, bool> allowed)
    {
        var method = context.Request.Method;
        var reading = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        switch (rest)
        {
            case [var channel, var name] when reading && channel != "releases" && channel != "status":
                return Serve(store, held => held.Resolve(channel, name), context);
            case ["releases", var tag, var name] when reading:
                return Serve(store, held => held.ResolveIn(tag, name), context);
        }

        if (!allowed(context))
        {
            return Results.Json(
                new UpdateFailure("unauthorized", "the request carries no token of the releases"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (store is null)
        {
            return Off();
        }

        try
        {
            return await ChangeAsync(context, rest, store, method).ConfigureAwait(false);
        }
        catch (ReleaseRefused refused)
        {
            return Results.Json(new UpdateFailure(refused.Code, refused.Message, refused.File), statusCode: refused.Status);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Results.Json(new UpdateFailure("bad-request", "the body does not read as JSON of the request"), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> ChangeAsync(HttpContext context, string[] rest, ReleaseStore store, string method)
    {
        var ct = context.RequestAborted;
        switch (rest)
        {
            case ["status"] when HttpMethods.IsGet(method):
                return Results.Ok(store.Status());
            case ["releases"] when HttpMethods.IsGet(method):
                return Results.Ok(store.List(Number(context, "page", 1), Number(context, "size", 20)));
            case ["releases"] when HttpMethods.IsPost(method):
                var draft = await ReadAsync<ReleaseDraft>(context, ct).ConfigureAwait(false);

                return Results.Ok(await store.CreateAsync(draft.Version, draft.Channel, draft.Description, ct).ConfigureAwait(false));
            case ["releases", var tag] when HttpMethods.IsGet(method):
                return Results.Ok(store.Read(tag));
            case ["releases", var tag] when HttpMethods.IsPatch(method):
                var change = await ReadAsync<ReleaseChange>(context, ct).ConfigureAwait(false);

                return Results.Ok(await store.ChangeAsync(tag, change.Channel, change.Description, change.From, ct).ConfigureAwait(false));
            case ["releases", var tag] when HttpMethods.IsDelete(method):
                await store.DeleteAsync(tag, ct).ConfigureAwait(false);

                return Results.NoContent();
            case ["releases", var tag, "files", var name] when HttpMethods.IsPut(method):
                Unbound(context);

                return Results.Ok(await store.PutFileAsync(tag, name, context.Request.Body, context.Request.Query["from"].ToString(), ct).ConfigureAwait(false));
            case ["releases", var tag, "files", var name] when HttpMethods.IsDelete(method):
                return Results.Ok(await store.DropFileAsync(tag, name, ct).ConfigureAwait(false));
            case ["releases", var tag, "archive"] when HttpMethods.IsPost(method):
                Unbound(context);

                return Results.Ok(await store.ImportAsync(tag, context.Request.Body, ct).ConfigureAwait(false));
            default:
                return Results.NotFound();
        }
    }

    // Serves a file of a release, the manifest never cached.
    private static IResult Serve(ReleaseStore? store, Func<ReleaseStore, string?> resolve, HttpContext context)
    {
        if (store is null)
        {
            return Off();
        }

        if (resolve(store) is not { } path)
        {
            return Results.NotFound();
        }

        var name = Path.GetFileName(path);
        if (name == ReleaseManifest.FileName)
        {
            context.Response.Headers.CacheControl = "no-cache";
        }

        return Results.File(path, TypeOf(name), enableRangeProcessing: true);
    }

    private static async Task<T> ReadAsync<T>(HttpContext context, CancellationToken ct) =>
        await context.Request.ReadFromJsonAsync<T>(ct).ConfigureAwait(false)
        ?? throw new ReleaseRefused("bad-request", "the body is empty");

    private static int Number(HttpContext context, string name, int fallback) =>
        int.TryParse(context.Request.Query[name].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static IResult Off() =>
        Results.Json(new UpdateFailure("updates-off", "the releases are not served"), statusCode: StatusCodes.Status503ServiceUnavailable);

    // Lets a body of any size in: the store bounds it itself.
    private static void Unbound(HttpContext context)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = null;
        }
    }
}
