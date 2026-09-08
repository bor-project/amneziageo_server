namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Thrown when the host refuses a change to its network.
/// </summary>
public sealed class HostNetworkException : Exception
{
    /// <summary>
    /// ctor
    /// </summary>
    public HostNetworkException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// ctor
    /// </summary>
    public HostNetworkException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
