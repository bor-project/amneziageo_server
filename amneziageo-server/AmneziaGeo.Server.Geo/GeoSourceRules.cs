using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Geo;

/// <summary>
/// Shapes the settings of a geo source have to take.
/// </summary>
public static partial class GeoSourceRules
{
    /// <summary>
    /// The longest name a source takes.
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// The longest address a source is downloaded from.
    /// </summary>
    public const int MaxUrlLength = 512;

    /// <summary>
    /// Returns why the settings of a source are unusable, or null when they hold.
    /// </summary>
    public static GeoFault? Check(GeoSource source) =>
        CheckName(source.Name) ?? CheckKind(source.Kind) ?? CheckUrl(source.Url);

    /// <summary>
    /// Returns why a name is unusable, or null when it holds.
    /// </summary>
    public static GeoFault? CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new GeoFault("bad-name", "the name is empty");
        }

        if (name.Length > MaxNameLength)
        {
            return new GeoFault("bad-name", $"the name is longer than {MaxNameLength} characters");
        }

        return Name().IsMatch(name)
            ? null
            : new GeoFault("bad-name", "the name takes lower case letters, digits, a hyphen and a dot, and starts with a letter");
    }

    /// <summary>
    /// Returns why a kind is unusable, or null when it holds.
    /// </summary>
    public static GeoFault? CheckKind(string kind) => GeoKind.Known(kind)
        ? null
        : new GeoFault("bad-kind", $"the kind is '{GeoKind.Ip}' or '{GeoKind.Site}'");

    /// <summary>
    /// Returns why an address is unusable, or null when it holds.
    /// </summary>
    public static GeoFault? CheckUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new GeoFault("bad-url", "the address is empty");
        }

        if (url.Length > MaxUrlLength)
        {
            return new GeoFault("bad-url", $"the address is longer than {MaxUrlLength} characters");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            return new GeoFault("bad-url", $"'{url}' is not an http or https address");
        }

        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9.-]*$")]
    private static partial Regex Name();
}
