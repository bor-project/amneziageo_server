using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The page of the panel, carrying the path the panel sits under.
/// </summary>
public sealed class PanelIndex
{
    private const string Mark = "<base href=\"";

    private readonly string _file;

    private readonly string _prefix;

    private readonly Lock _sync = new();

    private string _held = string.Empty;

    private DateTime _stamp;

    /// <summary>
    /// ctor
    /// </summary>
    public PanelIndex(IWebHostEnvironment environment, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(settings);

        _file = Path.Combine(environment.WebRootPath ?? environment.ContentRootPath, "index.html");
        _prefix = settings.Prefix;
    }

    /// <summary>
    /// Returns the page, read again when the file behind it changes.
    /// </summary>
    public string Text()
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
                _held = Under(File.ReadAllText(_file), _prefix);
                _stamp = stamp;
            }

            return _held;
        }
    }

    /// <summary>
    /// Returns the page with the path the panel sits under written into it.
    /// </summary>
    public static string Under(string page, string prefix)
    {
        ArgumentNullException.ThrowIfNull(page);

        var at = page.IndexOf(Mark, StringComparison.Ordinal);
        if (at >= 0)
        {
            var from = at + Mark.Length;
            var to = page.IndexOf('"', from);

            return to < 0 ? page : page[..from] + prefix + page[to..];
        }

        var head = page.IndexOf("<head>", StringComparison.Ordinal);

        return head < 0 ? page : page.Insert(head + 6, "<base href=\"" + prefix + "\" />");
    }
}
