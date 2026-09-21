using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// A package of the panel for one architecture.
/// </summary>
/// <param name="Name">The file of the package among the files of the release.</param>
/// <param name="Arch">The architecture the package runs on.</param>
/// <param name="Size">The size of the package, in bytes.</param>
/// <param name="Sha256">The digest of the package.</param>
public sealed record UpdatePackage(string Name, string Arch, long Size, string Sha256);

/// <summary>
/// What a release of the panel carries, as its signed manifest names it.
/// </summary>
/// <param name="Version">The version of the panel in the release.</param>
/// <param name="Channel">The channel the release belongs to.</param>
/// <param name="Published">When the release was built.</param>
/// <param name="Commit">The commit the release was built from.</param>
/// <param name="Image">The image of the release, pinned to its digest.</param>
/// <param name="Packages">The packages of the release.</param>
public sealed partial record UpdateManifest(
    Version Version,
    string Channel,
    DateTimeOffset? Published,
    string Commit,
    string Image,
    IReadOnlyList<UpdatePackage> Packages)
{
    /// <summary>
    /// The largest manifest the panel reads, in bytes.
    /// </summary>
    public const int MaxSize = 64 * 1024;

    /// <summary>
    /// Reads a manifest and refuses one that does not hold together.
    /// </summary>
    public static UpdateManifest Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxSize)
        {
            throw new InvalidDataException("the manifest is larger than 64 kilobytes");
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return Read(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("the manifest does not read as JSON", ex);
        }
    }

    /// <summary>
    /// Returns the package for an architecture, or null when the release carries none.
    /// </summary>
    public UpdatePackage? PackageFor(string arch) =>
        Packages.FirstOrDefault(package => string.Equals(package.Arch, arch, StringComparison.Ordinal));

    private static UpdateManifest Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("the manifest is not a JSON object");
        }

        var version = Version.TryParse(Text(root, "version"), out var parsed)
            ? parsed
            : throw new InvalidDataException("the manifest names no version");
        var channel = Text(root, "channel") is { Length: > 0 } named ? named : UpdateOptions.Stable;
        var image = Text(root, "image");
        if (image.Length > 0 && !PinnedImage().IsMatch(image))
        {
            throw new InvalidDataException($"the image '{image}' is not pinned to a digest");
        }

        return new UpdateManifest(version, channel, Moment(Text(root, "published")), Text(root, "commit"), image, ListPackages(root));
    }

    private static List<UpdatePackage> ListPackages(JsonElement root)
    {
        var found = new List<UpdatePackage>();
        if (!root.TryGetProperty("packages", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var item in list.EnumerateArray())
        {
            found.Add(Package(item));
        }

        return found;
    }

    private static UpdatePackage Package(JsonElement item)
    {
        var name = Text(item, "name");
        if (!PackageFile().IsMatch(name))
        {
            throw new InvalidDataException($"'{name}' is not the file of a package");
        }

        var arch = Text(item, "arch");
        if (!ArchName().IsMatch(arch))
        {
            throw new InvalidDataException($"the package '{name}' names no architecture");
        }

        var sha = Text(item, "sha256").ToLowerInvariant();
        if (!Digest().IsMatch(sha))
        {
            throw new InvalidDataException($"the package '{name}' carries no digest");
        }

        return new UpdatePackage(name, arch, Size(item), sha);
    }

    private static long Size(JsonElement item) =>
        item.TryGetProperty("size", out var given)
        && given.ValueKind == JsonValueKind.Number
        && given.TryGetInt64(out var bytes)
        && bytes > 0
            ? bytes
            : 0;

    private static DateTimeOffset? Moment(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    [GeneratedRegex("^[a-z0-9][a-z0-9._/:-]*@sha256:[0-9a-f]{64}$")]
    private static partial Regex PinnedImage();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.tar\.gz$")]
    private static partial Regex PackageFile();

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ArchName();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Digest();
}
