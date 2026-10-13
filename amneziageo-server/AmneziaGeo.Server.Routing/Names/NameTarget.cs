using System.Buffers.Binary;
using System.Net;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Reads where a connection the firewall sent in was going, out of what the kernel keeps of it.
/// </summary>
public static class NameTarget
{
    private const int Inet = 2;

    private const int Inet6 = 10;

    private const int Size = 16;

    private const int Size6 = 28;

    /// <summary>
    /// Returns the address and the port a socket address of the kernel names, or null when it names none.
    /// </summary>
    public static IPEndPoint? Read(ReadOnlySpan<byte> name)
    {
        if (name.Length < Size)
        {
            return null;
        }

        var family = BitConverter.ToUInt16(name);
        var port = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        if (family == Inet)
        {
            return new IPEndPoint(new IPAddress(name.Slice(4, 4)), port);
        }

        return family == Inet6 && name.Length >= Size6 ? new IPEndPoint(new IPAddress(name.Slice(8, 16)), port) : null;
    }
}
