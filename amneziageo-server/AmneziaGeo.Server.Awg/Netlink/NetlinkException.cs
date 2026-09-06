namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// A failure reported by the kernel or by the socket underneath.
/// </summary>
public sealed class NetlinkException : Exception
{
    /// <summary>
    /// ctor
    /// </summary>
    public NetlinkException(string message, int error = 0)
        : base(error == 0 ? message : $"{message} (errno {error})")
    {
        Error = error;
    }

    /// <summary>
    /// The errno the kernel returned.
    /// </summary>
    public int Error { get; }
}
