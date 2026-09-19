using System.Net.Sockets;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// Puts the mark of an outbound on a socket.
/// </summary>
public static class OutboundMark
{
    private const int SocketLevel = 1;

    private const int MarkOption = 36;

    /// <summary>
    /// Marks a socket so what it sends leaves through the outbound, telling whether the mark took.
    /// </summary>
    public static bool Put(Socket socket, uint mark)
    {
        ArgumentNullException.ThrowIfNull(socket);

        if (mark == 0 || !OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            socket.SetRawSocketOption(SocketLevel, MarkOption, BitConverter.GetBytes(mark));

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
