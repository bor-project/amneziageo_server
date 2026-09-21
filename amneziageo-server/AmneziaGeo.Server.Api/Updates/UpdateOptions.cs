namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Where the panel looks for its releases and how it puts one on.
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>
    /// The channel that takes releases alone.
    /// </summary>
    public const string Stable = "stable";

    /// <summary>
    /// The channel that takes the builds before a release as well.
    /// </summary>
    public const string Test = "test";

    /// <summary>
    /// The repository whose releases carry the panel.
    /// </summary>
    public string Repository { get; set; } = "bor-project/amneziageo_server";

    /// <summary>
    /// The channel releases are taken from.
    /// </summary>
    public string Channel { get; set; } = Stable;

    /// <summary>
    /// The address of a manifest read in place of the releases of the repository.
    /// </summary>
    public string Manifest { get; set; } = string.Empty;

    /// <summary>
    /// The file of the public key manifests are checked against, in place of the key the panel carries.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// How often releases are looked for, in hours; zero leaves it to the panel.
    /// </summary>
    public double CheckHours { get; set; } = 12;

    /// <summary>
    /// The directory downloads and the record of the last update are kept in, beside the database when empty.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// The socket of the Docker daemon.
    /// </summary>
    public string Docker { get; set; } = "/var/run/docker.sock";

    /// <summary>
    /// Tells whether the builds before a release are taken.
    /// </summary>
    public bool TakesTests => string.Equals(Channel, Test, StringComparison.OrdinalIgnoreCase);
}
