using System.Globalization;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Where the panel hands the subscriptions of the clients out and what it tells the clients about them.
/// </summary>
public sealed record SubscriptionSettings
{
    /// <summary>
    /// Whether the panel hands the subscriptions out.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Whether the subscriptions answer on a port of their own instead of the port of the services of each endpoint.
    /// </summary>
    public bool Separate { get; init; }

    /// <summary>
    /// The addresses the subscriptions are served on, empty for every address of the host.
    /// </summary>
    public IReadOnlyList<string> Listen { get; init; } = [];

    /// <summary>
    /// The names the subscriptions answer to, empty for any.
    /// </summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>
    /// The port the subscriptions are served on.
    /// </summary>
    public int Port { get; init; } = SubscriptionDefaults.Port;

    /// <summary>
    /// The path the subscriptions sit under.
    /// </summary>
    public string Path { get; init; } = SubscriptionDefaults.Path;

    /// <summary>
    /// Whether the panel holds the port of the subscriptions open in the firewall of the host.
    /// </summary>
    public bool Opened { get; init; }

    /// <summary>
    /// The certificate chain the subscriptions answer under, empty for the one of the panel.
    /// </summary>
    public string Certificate { get; init; } = string.Empty;

    /// <summary>
    /// The key of the certificate the subscriptions answer under.
    /// </summary>
    public string CertificateKey { get; init; } = string.Empty;

    /// <summary>
    /// How often a client reads the subscription again, in hours.
    /// </summary>
    public int UpdateHours { get; init; } = SubscriptionDefaults.UpdateHours;

    /// <summary>
    /// The name a client gives the subscription, empty for the host of its address.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Returns the addresses with the port the subscriptions bind.
    /// </summary>
    public IReadOnlyList<string> Entries => Listen.Count == 0
        ? [Any + ":" + Port.ToString(CultureInfo.InvariantCulture)]
        : [.. Listen.Select(address => Bind(address) + ":" + Port.ToString(CultureInfo.InvariantCulture))];

    /// <summary>
    /// Returns the path the subscriptions sit under, bounded by slashes.
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
    /// Tells whether the subscriptions have to be bound anew to answer as the other settings say.
    /// </summary>
    public bool Rebinds(SubscriptionSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return IsEnabled != other.IsEnabled
            || Separate != other.Separate
            || !Listen.SequenceEqual(other.Listen, StringComparer.Ordinal)
            || Port != other.Port
            || !string.Equals(Certificate, other.Certificate, StringComparison.Ordinal)
            || !string.Equals(CertificateKey, other.CertificateKey, StringComparison.Ordinal);
    }

    private const string Any = "*";

    private static string Bind(string address) =>
        address.Contains(':', StringComparison.Ordinal) ? "[" + address + "]" : address;
}
