using System.Globalization;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Where the panel answers from and in which language it opens.
/// </summary>
public sealed record PanelSettings
{
    /// <summary>
    /// The addresses the panel listens on, empty for every address of the host.
    /// </summary>
    public IReadOnlyList<string> Listen { get; init; } = [];

    /// <summary>
    /// The names the panel answers to, empty for any.
    /// </summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>
    /// The port the panel listens on.
    /// </summary>
    public int Port { get; init; } = PanelDefaults.Port;

    /// <summary>
    /// The path the panel sits under, empty for the root.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Whether the panel holds its own port open in the firewall of the host.
    /// </summary>
    public bool Opened { get; init; }

    /// <summary>
    /// The certificate chain the panel answers under, empty for the one the configuration names.
    /// </summary>
    public string Certificate { get; init; } = string.Empty;

    /// <summary>
    /// The key of the certificate the panel answers under.
    /// </summary>
    public string CertificateKey { get; init; } = string.Empty;

    /// <summary>
    /// The language the panel opens in.
    /// </summary>
    public string Language { get; init; } = PanelDefaults.Language;

    /// <summary>
    /// Returns the addresses with the port the panel binds.
    /// </summary>
    public IReadOnlyList<string> Entries => Listen.Count == 0
        ? [Any + ":" + Port.ToString(CultureInfo.InvariantCulture)]
        : [.. Listen.Select(address => Bind(address) + ":" + Port.ToString(CultureInfo.InvariantCulture))];

    /// <summary>
    /// Returns the path the panel sits under, bounded by slashes.
    /// </summary>
    public string Prefix
    {
        get
        {
            var trimmed = Path.Trim('/');

            return trimmed.Length == 0 ? "/" : "/" + trimmed + "/";
        }
    }

    /// <summary>
    /// Tells whether the panel has to start over to answer as the other settings say.
    /// </summary>
    public bool Differs(PanelSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return !Listen.SequenceEqual(other.Listen, StringComparer.Ordinal)
            || !Domains.SequenceEqual(other.Domains, StringComparer.OrdinalIgnoreCase)
            || Port != other.Port
            || !string.Equals(Prefix, other.Prefix, StringComparison.Ordinal)
            || !string.Equals(Certificate, other.Certificate, StringComparison.Ordinal)
            || !string.Equals(CertificateKey, other.CertificateKey, StringComparison.Ordinal);
    }

    private const string Any = "*";

    private static string Bind(string address) =>
        address.Contains(':', StringComparison.Ordinal) ? "[" + address + "]" : address;
}
