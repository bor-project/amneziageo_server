using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// The web interface of a service of the releases: its page and its files under a path of its own.
/// </summary>
public sealed class UpdateWindow : IDisposable
{
    /// <summary>
    /// The name of the page of the web interface among its files.
    /// </summary>
    public const string PageName = "index.html";

    /// <summary>
    /// The name of the mark on the page that carries the path of the releases.
    /// </summary>
    public const string ReleasesMark = "amneziageo-releases";

    /// <summary>
    /// The name of the mark on the page that carries the version of the service.
    /// </summary>
    public const string VersionMark = "amneziageo-version";

    private const string Letters = "abcdefghijklmnopqrstuvwxyz0123456789";

    private const int NameLength = 16;

    private const int LongestPath = 128;

    private const string Base = "<base href=\"";

    private const string Head = "<head>";

    private const string Assets = "assets/";

    private static readonly string[] Words = ["stable", "beta", "test", "releases", "status", "health"];

    private static readonly FileExtensionContentTypeProvider Types = new();

    private readonly PhysicalFileProvider? _files;

    private readonly string _page;

    private readonly string _releases;

    private readonly string _version;

    private readonly Lock _sync = new();

    private string _held = string.Empty;

    private DateTime _stamp;

    /// <summary>
    /// ctor
    /// </summary>
    /// <param name="root">The directory the files of the web interface lie in.</param>
    /// <param name="path">The path the web interface answers under.</param>
    /// <param name="releases">The path the releases answer under, a slash or empty for the root.</param>
    /// <param name="version">The version of the service.</param>
    public UpdateWindow(string root, string path, string releases, string version)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(version);

        Prefix = "/" + path.Trim('/') + "/";
        _releases = PrefixOf(releases);
        _version = version;
        _page = Path.Combine(root, PageName);
        _files = Directory.Exists(root) ? new PhysicalFileProvider(Path.GetFullPath(root)) : null;
    }

    /// <summary>
    /// The path the web interface answers under, with a slash at each end.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Tells whether the page of the web interface is there.
    /// </summary>
    public bool Present => File.Exists(_page);

    /// <summary>
    /// Makes a path for the web interface.
    /// </summary>
    public static string Make() => RandomNumberGenerator.GetString(Letters, NameLength);

    /// <summary>
    /// Tells whether a path may hold the web interface beside the releases: names of letters, digits, dots, dashes and
    /// underscores, none of them a route of the releases.
    /// </summary>
    /// <param name="path">The path of the web interface.</param>
    /// <param name="releases">The path the releases answer under, a slash or empty for the root.</param>
    public static bool Fits(string path, string releases)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(releases);

        var trimmed = path.Trim('/');
        if (trimmed.Length == 0 || trimmed.Length > LongestPath || !trimmed.Split('/').All(Named))
        {
            return false;
        }

        var lead = releases.Trim('/');
        if (lead.Length == 0)
        {
            return !Words.Contains(trimmed.Split('/')[0], StringComparer.Ordinal);
        }

        if (trimmed == lead)
        {
            return false;
        }

        return !trimmed.StartsWith(lead + "/", StringComparison.Ordinal)
            || !Words.Contains(trimmed[(lead.Length + 1)..].Split('/')[0], StringComparer.Ordinal);
    }

    /// <summary>
    /// Answers a request that comes under the path of the web interface; false when it comes under something else.
    /// </summary>
    public async Task<bool> TryAnswerAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var under = new PathString(Prefix.TrimEnd('/'));
        if (context.Request.Path == under)
        {
            context.Response.Redirect(Prefix + context.Request.QueryString);

            return true;
        }

        if (!context.Request.Path.StartsWithSegments(under, StringComparison.Ordinal, out var rest))
        {
            return false;
        }

        Guard(context.Response);
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";

            return true;
        }

        var name = (rest.Value ?? string.Empty).TrimStart('/');
        if (name.Length > 0 && name != PageName && _files?.GetFileInfo(name) is { Exists: true, IsDirectory: false, PhysicalPath: { } file })
        {
            context.Response.Headers.CacheControl = name.StartsWith(Assets, StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-cache";
            await Results.File(file, TypeOf(name)).ExecuteAsync(context).ConfigureAwait(false);

            return true;
        }

        var page = Page();
        if (name.StartsWith(Assets, StringComparison.Ordinal) || page.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return true;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.WriteAsync(page, context.RequestAborted).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Returns the page with the path of the web interface, the path of the releases and the version of the service
    /// written into it.
    /// </summary>
    public static string Fill(string page, string prefix, string releases, string version)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(version);

        var based = Based(page, prefix);
        var head = based.IndexOf(Head, StringComparison.Ordinal);
        if (head < 0)
        {
            return based;
        }

        var marks = Mark(ReleasesMark, PrefixOf(releases)) + Mark(VersionMark, version);

        return based.Insert(head + Head.Length, marks);
    }

    /// <summary>
    /// Lets the files of the web interface go.
    /// </summary>
    public void Dispose() => _files?.Dispose();

    // Returns the page, read again when the file behind it changes.
    private string Page()
    {
        lock (_sync)
        {
            if (!File.Exists(_page))
            {
                return string.Empty;
            }

            var stamp = File.GetLastWriteTimeUtc(_page);
            if (_held.Length == 0 || stamp != _stamp)
            {
                _held = Fill(File.ReadAllText(_page), Prefix, _releases, _version);
                _stamp = stamp;
            }

            return _held;
        }
    }

    private static string Based(string page, string prefix)
    {
        var at = page.IndexOf(Base, StringComparison.Ordinal);
        if (at >= 0)
        {
            var from = at + Base.Length;
            var to = page.IndexOf('"', from);

            return to < 0 ? page : page[..from] + WebUtility.HtmlEncode(prefix) + page[to..];
        }

        var head = page.IndexOf(Head, StringComparison.Ordinal);

        return head < 0 ? page : page.Insert(head + Head.Length, Base + WebUtility.HtmlEncode(prefix) + "\" />");
    }

    private static string Mark(string name, string value) => $"<meta name=\"{name}\" content=\"{WebUtility.HtmlEncode(value)}\" />";

    private static string PrefixOf(string path)
    {
        var trimmed = path.Trim('/');

        return trimmed.Length == 0 ? "/" : "/" + trimmed + "/";
    }

    private static bool Named(string part) =>
        part.Length > 0 && part != "." && part != ".." && part.All(one => char.IsAsciiLetterOrDigit(one) || one is '.' or '-' or '_');

    private static string TypeOf(string name) => Types.TryGetContentType(name, out var type) ? type : "application/octet-stream";

    // Keeps the web interface out of frames of other sites and its address out of the requests it leads to.
    private static void Guard(HttpResponse response)
    {
        response.Headers.XFrameOptions = "DENY";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
