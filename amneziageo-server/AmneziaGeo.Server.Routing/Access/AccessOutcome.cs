namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// What came back to a connection that went out.
/// </summary>
public static class AccessOutcome
{
    /// <summary>
    /// The other side answered: with data over TCP, with any packet over the other protocols.
    /// </summary>
    public const string Ok = "ok";

    /// <summary>
    /// The other side took the TCP connection and sent nothing in time.
    /// </summary>
    public const string Empty = "empty";

    /// <summary>
    /// The other side reset or closed the TCP connection before it sent anything.
    /// </summary>
    public const string Reset = "reset";

    /// <summary>
    /// Nothing came back in time.
    /// </summary>
    public const string Silent = "silent";

    /// <summary>
    /// An error came back that the address or the port cannot be reached.
    /// </summary>
    public const string Unreachable = "unreachable";

    /// <summary>
    /// Asks for every outcome of a connection that did not get through.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Every outcome a record can carry.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Ok, Empty, Reset, Silent, Unreachable];

    /// <summary>
    /// The outcomes of the connections that did not get through.
    /// </summary>
    public static readonly IReadOnlyList<string> Failures = [Empty, Reset, Silent, Unreachable];
}
