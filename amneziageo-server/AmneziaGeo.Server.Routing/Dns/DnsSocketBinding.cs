using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Binds DNS sockets to their selected path.
/// </summary>
internal static class DnsSocketBinding
{
    internal static void Apply(Socket socket, IPEndPoint server, uint mark, string? device, IReadOnlyList<string> sourceAddresses)
    {
        if (!OutboundMark.Put(socket, mark))
        {
            throw new IOException("the mark of the DNS path did not take");
        }

        if (string.IsNullOrEmpty(device))
        {
            return;
        }

        if (!OperatingSystem.IsLinux() || OutboundRules.CheckName(device) is not null)
        {
            throw new IOException("the selected DNS interface is unavailable");
        }

        var source = sourceAddresses.Select(text => AwgAllowedIp.TryParse(text, out var range) ? range.Address : null)
            .OfType<IPAddress>().FirstOrDefault(address => address.AddressFamily == server.AddressFamily);
        if (source is null)
        {
            throw new IOException("the selected DNS interface has no source address in this family");
        }

        socket.SetRawSocketOption(1, 25, Encoding.UTF8.GetBytes(device + '\0'));
        if (server.AddressFamily == AddressFamily.InterNetwork && IsLocal(server.Address)
            && !AcceptsLocal(device))
        {
            throw new IOException("the selected DNS interface cannot receive a name server address also held by this host");
        }

        socket.Bind(new IPEndPoint(source, 0));
    }

    private static bool AcceptsLocal(string device)
    {
        try
        {
            return File.ReadAllText($"/proc/sys/net/ipv4/conf/{device}/accept_local").Trim() == "1";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("{0}", ex);

            throw new IOException("the selected DNS interface policy was not read", ex);
        }
    }

    private static bool IsLocal(IPAddress address)
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().SelectMany(one => one.GetIPProperties().UnicastAddresses)
                .Any(one => one.Address.Equals(address));
        }
        catch (NetworkInformationException ex)
        {
            Trace.TraceWarning("{0}", ex);

            throw new IOException("the local addresses of the DNS path were not read", ex);
        }
    }
}
