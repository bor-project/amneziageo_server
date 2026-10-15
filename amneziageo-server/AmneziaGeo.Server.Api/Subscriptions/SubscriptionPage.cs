using System.Text.Json;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// The page a browser reads a subscription as, built with the panel.
/// </summary>
public sealed class SubscriptionPage
{
    /// <summary>
    /// The policy the page answers under: its own inline script and styles and the pictures it draws, nothing else.
    /// </summary>
    public const string Policy =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>
    /// What the build of the panel leaves in the page where the subscription goes.
    /// </summary>
    public const string Mark = "\"%SUBSCRIPTION%\"";

    private const string FileName = "subscription.html";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _file;

    private readonly Lock _sync = new();

    private string _held = string.Empty;

    private DateTime _stamp;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionPage(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        _file = file;
    }

    /// <summary>
    /// Returns the page the panel was built with.
    /// </summary>
    public static SubscriptionPage Of(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new SubscriptionPage(Path.Combine(environment.WebRootPath ?? environment.ContentRootPath, FileName));
    }

    /// <summary>
    /// Returns the page carrying a subscription, empty when the panel was built without it.
    /// </summary>
    public string Text(SubscriptionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        var page = Shell();
        var at = page.IndexOf(Mark, StringComparison.Ordinal);

        return at < 0 ? string.Empty : page[..at] + JsonSerializer.Serialize(view, Web) + page[(at + Mark.Length)..];
    }

    // The page as the build left it, read again when the file behind it changes.
    private string Shell()
    {
        lock (_sync)
        {
            if (!File.Exists(_file))
            {
                return string.Empty;
            }

            var stamp = File.GetLastWriteTimeUtc(_file);
            if (_held.Length == 0 || stamp != _stamp)
            {
                _held = File.ReadAllText(_file);
                _stamp = stamp;
            }

            return _held;
        }
    }
}
