namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// One new connection of a client as the log keeps it.
/// </summary>
public sealed record AccessRecord
{
    /// <summary>
    /// The number of the record in the log.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// When the connection was decided.
    /// </summary>
    public DateTimeOffset At { get; init; }

    /// <summary>
    /// The client the connection came from, or empty when the panel holds no client with its address.
    /// </summary>
    public string Client { get; init; } = string.Empty;

    /// <summary>
    /// The address the connection came from.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The port the connection came from.
    /// </summary>
    public int SourcePort { get; init; }

    /// <summary>
    /// The interface the connection came in on.
    /// </summary>
    public string Inbound { get; init; } = string.Empty;

    /// <summary>
    /// The number of the transport protocol.
    /// </summary>
    public int Protocol { get; init; }

    /// <summary>
    /// The address the connection went to.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// The port the connection went to.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The name the resolver answered with the address for, or empty.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// How the connection was carried.
    /// </summary>
    public string Verdict { get; init; } = AccessVerdict.Host;

    /// <summary>
    /// The rule that decided, or null when none did.
    /// </summary>
    public long? Rule { get; init; }

    /// <summary>
    /// The name of the rule that decided, or empty.
    /// </summary>
    public string RuleName { get; init; } = string.Empty;

    /// <summary>
    /// The outbound the connection left through, the one a held rule waits for, or the guard that dropped it.
    /// </summary>
    public string Way { get; init; } = string.Empty;

    /// <summary>
    /// The balancer that picked the outbound, or empty.
    /// </summary>
    public string Via { get; init; } = string.Empty;

    /// <summary>
    /// Whether the connection left through the uplink of the host or was handed to another server, or empty.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// What came back to the connection, or empty when it did not go out or nobody watched it.
    /// </summary>
    public string Outcome { get; init; } = string.Empty;
}
