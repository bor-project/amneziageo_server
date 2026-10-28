namespace AmneziaGeo.Server.Updates.Host;

/// <summary>
/// Where the service answers from and where it keeps the releases, the token and the certificate.
/// </summary>
public sealed class UpdatesOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Updates";

    /// <summary>
    /// The port the service answers on when the configuration names nothing.
    /// </summary>
    public const int Port = 8445;

    /// <summary>
    /// Addresses and ports the service listens on, a star for every address of the host; empty for the port of the
    /// service on every address.
    /// </summary>
    public string[] Listen { get; set; } = [];

    /// <summary>
    /// The path the releases sit under, empty for the root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The directory the releases are kept in.
    /// </summary>
    public string Directory { get; set; } = "/var/lib/amneziageo-updates";

    /// <summary>
    /// The directory the token and the certificate of the service lie in.
    /// </summary>
    public string Etc { get; set; } = "/etc/amneziageo-updates";

    /// <summary>
    /// The token the releases are changed with, empty to read it from its file.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The file the token is kept in, written with a new token when it is not there.
    /// </summary>
    public string TokenFile { get; set; } = string.Empty;

    /// <summary>
    /// The path the web interface answers under, empty to read it from its file.
    /// </summary>
    public string WebPath { get; set; } = string.Empty;

    /// <summary>
    /// The certificate chain the service answers under, in PEM; empty for one of its own.
    /// </summary>
    public string Certificate { get; set; } = string.Empty;

    /// <summary>
    /// The private key of the certificate, in PEM.
    /// </summary>
    public string CertificateKey { get; set; } = string.Empty;

    /// <summary>
    /// Returns the file the token is kept in.
    /// </summary>
    public string TokenPath => TokenFile.Length > 0 ? TokenFile : System.IO.Path.Combine(Etc, "token");

    /// <summary>
    /// Returns the file the path of the web interface is kept in.
    /// </summary>
    public string WebPathFile => System.IO.Path.Combine(Etc, "web-path");

    /// <summary>
    /// Returns the addresses the service listens on, the port of the service on every address when none is named.
    /// </summary>
    public IReadOnlyList<string> Entries => Listen.Length > 0 ? Listen : [$"*:{Port}"];
}
