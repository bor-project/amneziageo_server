using System.Diagnostics;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// Keeps the processes the server starts off the socket systemd takes the notifications of the service through.
/// </summary>
public static class ServiceSocket
{
    /// <summary>
    /// The variable systemd names the socket in.
    /// </summary>
    public const string Variable = "NOTIFY_SOCKET";

    /// <summary>
    /// Takes the socket out of the environment a process is started with.
    /// </summary>
    public static ProcessStartInfo Withhold(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);

        start.Environment.Remove(Variable);

        return start;
    }
}
