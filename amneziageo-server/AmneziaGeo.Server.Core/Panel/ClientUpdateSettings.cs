namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// Where the releases of the client are served from and how the clients learn of them.
/// </summary>
public sealed record ClientUpdateSettings
{
    /// <summary>
    /// Whether the panel offers the releases of the client at all.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Where the releases come from: a store of the panel or a service that stands apart.
    /// </summary>
    public string Source { get; init; } = ClientUpdateDefaults.Own;

    /// <summary>
    /// The address of the service that stands apart, empty for a store of the panel.
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// The token the releases are changed with: the one the service apart takes, or the one the store of the panel
    /// hands to whoever uploads from outside.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// The directory the store of the panel keeps the releases in, empty for the one beside the database.
    /// </summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>
    /// The port the store of the panel answers on, 0 for the port of the panel.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The path the store of the panel answers under.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Whether a client takes this server for its updates before it picks one itself.
    /// </summary>
    public bool Default { get; init; } = true;

    /// <summary>
    /// Tells whether the releases come from a store of the panel.
    /// </summary>
    public bool Own => string.Equals(Source, ClientUpdateDefaults.Own, StringComparison.Ordinal);

    /// <summary>
    /// Returns the path the store answers under, led by a slash and without one at the end.
    /// </summary>
    public string Prefix => "/" + Path.Trim('/');

    /// <summary>
    /// Returns the port the store answers on next to the port of the panel.
    /// </summary>
    public int PortOr(int panel) => Port > 0 ? Port : panel;

    /// <summary>
    /// Tells whether the releases have to be served anew to answer as the other settings say.
    /// </summary>
    public bool Rebinds(ClientUpdateSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return IsEnabled != other.IsEnabled
            || !string.Equals(Source, other.Source, StringComparison.Ordinal)
            || !string.Equals(Address, other.Address, StringComparison.Ordinal)
            || !string.Equals(Token, other.Token, StringComparison.Ordinal)
            || !string.Equals(Directory, other.Directory, StringComparison.Ordinal)
            || Port != other.Port
            || !string.Equals(Prefix, other.Prefix, StringComparison.Ordinal);
    }
}
