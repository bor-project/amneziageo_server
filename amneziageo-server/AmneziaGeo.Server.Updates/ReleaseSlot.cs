namespace AmneziaGeo.Server.Updates;

/// <summary>
/// One file a release carries: where it installs and what it is named.
/// </summary>
/// <param name="Name">The name of the file in the release.</param>
/// <param name="Platform">The system the file installs on: windows, linux or android.</param>
/// <param name="Arch">The architecture the file installs on.</param>
/// <param name="Variant">The kind of the file for its platform.</param>
/// <param name="Installer">The installer a patch goes with, null for an installer itself.</param>
public sealed record ReleaseSlot(string Name, string Platform, string Arch, string Variant, string? Installer = null)
{
    /// <summary>
    /// Tells whether the file is a patch.
    /// </summary>
    public bool IsPatch => Installer is not null;

    /// <summary>
    /// Returns the extension of the file, in lower case.
    /// </summary>
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();
}

/// <summary>
/// The files a release of the client is made of.
/// </summary>
public static class ReleaseSlots
{
    /// <summary>
    /// The system of the installers for Windows.
    /// </summary>
    public const string Windows = "windows";

    /// <summary>
    /// The system of the packages for Linux.
    /// </summary>
    public const string Linux = "linux";

    /// <summary>
    /// The system of the packages for Android.
    /// </summary>
    public const string Android = "android";

    /// <summary>
    /// The installer of Windows that carries .NET.
    /// </summary>
    public const string SelfContained = "scd";

    /// <summary>
    /// The installer of Windows that takes .NET from the machine.
    /// </summary>
    public const string FrameworkDependent = "fdd";

    /// <summary>
    /// The package of Android.
    /// </summary>
    public const string Apk = "apk";

    /// <summary>
    /// The extension of a patch.
    /// </summary>
    public const string PatchExtension = ".patch";

    /// <summary>
    /// How many files a whole release carries.
    /// </summary>
    public const int Count = 20;

    /// <summary>
    /// Returns the files a release of a version is made of: the installers of Windows, the packages of Linux, the
    /// packages of Android, each installer followed by its patch where the platform takes one.
    /// </summary>
    public static IReadOnlyList<ReleaseSlot> Of(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var v = version.ToString();
        var slots = new List<ReleaseSlot>(Count);
        foreach (var arch in new[] { "x64", "arm64" })
        {
            Patched(slots, $"AmneziaGeo-{v}-win-{arch}.exe", Windows, arch, SelfContained);
            Patched(slots, $"AmneziaGeo-{v}-win-{arch}-fdd.exe", Windows, arch, FrameworkDependent);
        }

        foreach (var package in new[] { "amneziageo", "amneziageo-gui" })
        {
            foreach (var arch in new[] { "amd64", "arm64" })
            {
                Patched(slots, $"{package}_{v}_{arch}.deb", Linux, arch, package);
            }
        }

        slots.Add(new ReleaseSlot($"AmneziaGeo-{v}-android.apk", Android, "universal", Apk));
        slots.Add(new ReleaseSlot($"AmneziaGeo-{v}-android-arm64.apk", Android, "arm64", Apk));
        slots.Add(new ReleaseSlot($"AmneziaGeo-{v}-android-arm.apk", Android, "arm", Apk));
        slots.Add(new ReleaseSlot($"AmneziaGeo-{v}-android-x64.apk", Android, "x64", Apk));

        return slots;
    }

    /// <summary>
    /// Returns the file of a release a name stands for, null for a name the release does not carry.
    /// </summary>
    public static ReleaseSlot? Find(Version version, string? name) =>
        name is null ? null : Of(version).FirstOrDefault(slot => string.Equals(slot.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Returns the installer the patch of a name goes with, in the order of the files.
    /// </summary>
    public static ReleaseSlot? InstallerOf(Version version, ReleaseSlot patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        return patch.Installer is null ? null : Find(version, patch.Installer);
    }

    private static void Patched(List<ReleaseSlot> slots, string name, string platform, string arch, string variant)
    {
        slots.Add(new ReleaseSlot(name, platform, arch, variant));
        slots.Add(new ReleaseSlot(Path.ChangeExtension(name, PatchExtension), platform, arch, variant, name));
    }
}
