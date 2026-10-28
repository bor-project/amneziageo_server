using System.Net;
using AmneziaGeo.Server.Updates;
using AmneziaGeo.Server.Updates.Host;

var builder = WebApplication.CreateSlimBuilder(args);
var options = new UpdatesOptions();
builder.Configuration.GetSection(UpdatesOptions.Section).Bind(options);
var certificate = HostCertificate.Of(options);
var (token, made) = Token(options);
var store = new ReleaseStore(options.Directory);
var version = typeof(UpdatesOptions).Assembly.GetName().Version?.ToString() ?? "0.0.0";
using var window = new UpdateWindow(Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), WebPath(options), options.Path, version);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = null;
    foreach (var entry in options.Entries)
    {
        var (address, port) = Parse(entry);
        if (address is null)
        {
            kestrel.ListenAnyIP(port, listen => listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => certificate.Current()));
        }
        else
        {
            kestrel.Listen(address, port, listen => listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => certificate.Current()));
        }
    }
});

var app = builder.Build();
app.Logger.LogInformation(
    "the releases answer from {Listen} under {Path}, kept in {Directory}",
    string.Join(", ", options.Entries),
    "/" + options.Path.Trim('/'),
    store.Directory);
if (made)
{
    app.Logger.LogInformation("a new token of the releases is kept in {File}", options.TokenPath);
}

var status = store.Status();
if (!status.Writable)
{
    app.Logger.LogWarning("{Fault}", status.Fault);
}

if (!window.Present)
{
    app.Logger.LogWarning("the web interface is not there: {Page} is missing", Path.Combine(builder.Environment.ContentRootPath, "wwwroot", UpdateWindow.PageName));
}

app.Run(async context =>
{
    if (context.Request.Path == "/health")
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;

        return;
    }

    if (await window.TryAnswerAsync(context).ConfigureAwait(false))
    {
        return;
    }

    if (!await UpdateApi.TryAnswerAsync(context, options.Path, store, asking => UpdateToken.Holds(asking, token)).ConfigureAwait(false))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }
});

await app.RunAsync().ConfigureAwait(false);

return 0;

// Reads the token from the options or its file, making and writing one when there is none.
static (string Token, bool Made) Token(UpdatesOptions options)
{
    if (options.Token.Length > 0)
    {
        return (options.Token, false);
    }

    var path = options.TokenPath;
    if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } held)
    {
        return (held, false);
    }

    var token = UpdateToken.Make();
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    File.WriteAllText(path, token + "\n");
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    return (token, true);
}

// Reads the path of the web interface from the options or its file, making and writing one when there is none.
static string WebPath(UpdatesOptions options)
{
    var path = options.WebPath;
    if (path.Length == 0 && File.Exists(options.WebPathFile))
    {
        path = File.ReadAllText(options.WebPathFile).Trim();
    }

    if (path.Length == 0)
    {
        path = UpdateWindow.Make();
        Directory.CreateDirectory(Path.GetDirectoryName(options.WebPathFile) ?? ".");
        File.WriteAllText(options.WebPathFile, path + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(options.WebPathFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    if (!UpdateWindow.Fits(path, options.Path))
    {
        throw new InvalidOperationException($"'{path}' cannot hold the web interface: names of letters, digits, dots, dashes and underscores, none of them a route of the releases");
    }

    return path;
}

// Reads an entry of the listen list: a star or an address, and a port.
static (IPAddress? Address, int Port) Parse(string entry)
{
    var trimmed = entry.Trim();
    if (trimmed.StartsWith("*:", StringComparison.Ordinal) && int.TryParse(trimmed[2..], out var any))
    {
        return (null, any);
    }

    if (int.TryParse(trimmed, out var bare))
    {
        return (null, bare);
    }

    if (IPEndPoint.TryParse(trimmed, out var point) && point.Port > 0)
    {
        return (point.Address, point.Port);
    }

    throw new InvalidOperationException($"'{entry}' is not an address to listen on: use *:port or address:port");
}
