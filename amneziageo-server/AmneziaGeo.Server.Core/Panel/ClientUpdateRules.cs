namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the settings of the releases of the client are allowed to hold.
/// </summary>
public static class ClientUpdateRules
{
    /// <summary>
    /// The longest address of a service apart.
    /// </summary>
    public const int MaxAddressLength = 256;

    /// <summary>
    /// The longest token.
    /// </summary>
    public const int MaxTokenLength = 128;

    private static readonly string[] Held = ["api", "assets", "sub"];

    /// <summary>
    /// Returns the first rule the settings break next to the panel they may share a port with.
    /// </summary>
    public static PanelFault? Check(ClientUpdateSettings settings, PanelSettings panel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(panel);

        if (Array.IndexOf(ClientUpdateDefaults.Sources, settings.Source) < 0)
        {
            return new PanelFault("bad-updates-source", $"the source of the releases is one of {string.Join(", ", ClientUpdateDefaults.Sources)}");
        }

        return settings.Own
            ? CheckPort(settings.Port) ?? CheckDirectory(settings.Directory) ?? CheckPath(settings.Path) ?? CheckShared(settings, panel) ?? CheckToken(settings.Token)
            : CheckAddress(settings.Address) ?? CheckToken(settings.Token);
    }

    private static PanelFault? CheckPort(int port) => port is < 0 or > 65535
        ? new PanelFault("bad-port", "the port of the releases is outside 1 to 65535")
        : null;

    private static PanelFault? CheckDirectory(string directory)
    {
        if (directory.Length == 0)
        {
            return null;
        }

        var shaped = directory.Length <= PanelRules.MaxFileLength
            && System.IO.Path.IsPathRooted(directory)
            && !directory.Contains("..", StringComparison.Ordinal)
            && !directory.Any(char.IsControl);

        return shaped ? null : new PanelFault("bad-updates-directory", "the directory of the releases is a full path without '..'");
    }

    private static PanelFault? CheckPath(string path)
    {
        var shaped = path.Length <= PanelRules.MaxPathLength
            && path.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_' or '.' or '/')
            && !path.Contains("..", StringComparison.Ordinal)
            && path.Trim('/').Length > 0;

        return shaped ? null : new PanelFault("bad-updates-path", $"'{path}' is not a path the releases can sit under");
    }

    private static PanelFault? CheckShared(ClientUpdateSettings settings, PanelSettings panel)
    {
        if (settings.PortOr(panel.Port) != panel.Port)
        {
            return null;
        }

        var head = Head(settings.Path);
        var taken = Array.Exists(Held, one => string.Equals(one, head, StringComparison.OrdinalIgnoreCase))
            || string.Equals(head, Head(panel.Path), StringComparison.OrdinalIgnoreCase);

        return taken
            ? new PanelFault("updates-path-taken", $"the panel answers under '/{head}/' on port {panel.Port}")
            : null;
    }

    private static PanelFault? CheckAddress(string address)
    {
        var shaped = address.Length is > 0 and <= MaxAddressLength
            && Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0;

        return shaped ? null : new PanelFault("bad-updates-address", "the address of the releases is an https address without a query");
    }

    private static PanelFault? CheckToken(string token) =>
        token.Length <= MaxTokenLength && !token.Any(letter => char.IsControl(letter) || char.IsWhiteSpace(letter))
            ? null
            : new PanelFault("bad-updates-token", $"the token is at most {MaxTokenLength} characters without spaces");

    private static string Head(string path) => path.Trim('/').Split('/')[0];
}
