using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// How the panel was put on the host.
/// </summary>
public static class UpdateModes
{
    /// <summary>
    /// The panel runs from somewhere its releases are not put on from the panel.
    /// </summary>
    public const string Manual = "manual";

    /// <summary>
    /// The panel runs from a release of its package.
    /// </summary>
    public const string Package = "package";

    /// <summary>
    /// The panel runs in a container.
    /// </summary>
    public const string Docker = "docker";

    /// <summary>
    /// The environment variable the image of the panel sets.
    /// </summary>
    public const string ContainerVariable = "AMNEZIAGEO_CONTAINER";

    /// <summary>
    /// Tells how the panel runs: in a container, from a release of the package, or from somewhere else.
    /// </summary>
    public static string Detect(string baseDirectory, Func<string, bool> exists, string? container)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (!string.IsNullOrEmpty(container) || exists("/.dockerenv"))
        {
            return Docker;
        }

        return exists(Path.Combine(baseDirectory, "install.sh")) && exists(Path.Combine(baseDirectory, "release"))
            ? Package
            : Manual;
    }

    /// <summary>
    /// Returns the name the packages of the releases give the architecture of the host.
    /// </summary>
    public static string Arch() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        var other => other.ToString().ToLowerInvariant(),
    };
}
