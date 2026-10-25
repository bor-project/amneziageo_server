using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// The patch beside an installer, as the manifest names it.
/// </summary>
/// <param name="Name">The file of the patch.</param>
/// <param name="Size">The size of the patch, in bytes.</param>
/// <param name="Sha256">The digest of the patch.</param>
/// <param name="From">The version the patch starts from, null when it is not named.</param>
public sealed record ManifestPatch(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("from")] string? From);

/// <summary>
/// One installer of a release, as the manifest names it.
/// </summary>
/// <param name="Name">The file of the installer.</param>
/// <param name="Platform">The system the installer installs on.</param>
/// <param name="Arch">The architecture the installer installs on.</param>
/// <param name="Variant">The kind of the installer for its platform.</param>
/// <param name="Sha256">The digest of the installer.</param>
/// <param name="Size">The size of the installer, in bytes.</param>
/// <param name="Patch">The patch beside the installer, null when the release carries none.</param>
public sealed record ManifestInstaller(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("arch")] string Arch,
    [property: JsonPropertyName("variant")] string Variant,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("patch")] ManifestPatch? Patch);

/// <summary>
/// What a release of the client carries, as the file beside its installers names it; the same shape the releases of
/// GitHub publish.
/// </summary>
/// <param name="Version">The version of the client in the release.</param>
/// <param name="Description">What the release says about itself.</param>
/// <param name="Channel">The channel the release goes out on.</param>
/// <param name="Setup">The installer a client of an old build takes, null when the release carries none.</param>
/// <param name="Installers">The installers of the release with their patches.</param>
public sealed record ReleaseManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("setup")] string? Setup,
    [property: JsonPropertyName("installers")] IReadOnlyList<ManifestInstaller> Installers)
{
    /// <summary>
    /// The file a release names itself in.
    /// </summary>
    public const string FileName = "update.json";

    /// <summary>
    /// The largest manifest that is read, in bytes.
    /// </summary>
    public const int MaxSize = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Builds the manifest of a release out of the files it holds: an installer goes in with the patch beside it,
    /// a patch without its installer stays out.
    /// </summary>
    public static ReleaseManifest Build(ReleaseTag tag, string description, IReadOnlyList<ReleaseFileInfo> files)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(files);

        var held = files.ToDictionary(file => file.Name, StringComparer.Ordinal);
        var installers = new List<ManifestInstaller>();
        foreach (var slot in ReleaseSlots.Of(tag.Version).Where(slot => !slot.IsPatch))
        {
            if (!held.TryGetValue(slot.Name, out var installer))
            {
                continue;
            }

            var patch = held.TryGetValue(Path.ChangeExtension(slot.Name, ReleaseSlots.PatchExtension), out var found)
                ? new ManifestPatch(found.Name, found.Size, found.Sha256, found.From.Length > 0 ? found.From : null)
                : null;
            installers.Add(new ManifestInstaller(slot.Name, slot.Platform, slot.Arch, slot.Variant, installer.Sha256, installer.Size, patch));
        }

        var setup = installers.FirstOrDefault(one =>
            one.Platform == ReleaseSlots.Windows && one.Arch == "x64" && one.Variant == ReleaseSlots.FrameworkDependent);

        return new ReleaseManifest(tag.Version.ToString(), description, tag.Channel, setup?.Name, installers);
    }

    /// <summary>
    /// Reads a manifest, null for text that is not one.
    /// </summary>
    public static ReleaseManifest? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            var read = JsonSerializer.Deserialize<ReleaseManifest>(json, Json);

            return read is { Version.Length: > 0 } ? read with { Installers = read.Installers ?? [] } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the manifest as its file is written.
    /// </summary>
    public string Serialize() => JsonSerializer.Serialize(this, Json) + "\n";
}
