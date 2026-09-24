using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Puts the TCP connections of the services on BBR where the kernel carries it.
/// </summary>
public static class TcpCongestion
{
    /// <summary>
    /// The congestion control the connections of the services take.
    /// </summary>
    public const string Bbr = "bbr";

    // IPPROTO_TCP and TCP_CONGESTION of Linux.
    private const int Tcp = 6;
    private const int Congestion = 13;

    /// <summary>
    /// Asks for BBR on a socket and tells whether the kernel took it.
    /// </summary>
    public static bool TryBbr(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            socket.SetRawSocketOption(Tcp, Congestion, "bbr"u8);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks for BBR on the socket under a connection of Kestrel.
    /// </summary>
    public static bool TryBbr(ConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.Features.Get<IConnectionSocketFeature>()?.Socket is { } socket && TryBbr(socket);
    }

    /// <summary>
    /// Reads the congestion control a socket runs, empty where the system does not tell.
    /// </summary>
    public static string Of(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (!OperatingSystem.IsLinux())
        {
            return string.Empty;
        }

        var buffer = new byte[16];
        try
        {
            var length = socket.GetRawSocketOption(Tcp, Congestion, buffer);
            var end = Array.IndexOf(buffer, (byte)0, 0, length);

            return System.Text.Encoding.ASCII.GetString(buffer, 0, end < 0 ? length : end);
        }
        catch (SocketException)
        {
            return string.Empty;
        }
    }
}
