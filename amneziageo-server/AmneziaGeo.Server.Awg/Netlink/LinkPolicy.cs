using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Sets IPv4 policy on a network interface.
/// </summary>
public static partial class LinkPolicy
{
    private const ushort SetLink = 19;
    private const ushort RequestAck = 5;
    private const ushort Nested = 0x8000;
    private const ushort AddressFamilies = 26;
    private const ushort Inet = 2;
    private const ushort InetConfiguration = 1;
    private const ushort AcceptLocalAddress = 23;

    /// <summary>
    /// Accepts replies from addresses also held by the host.
    /// </summary>
    public static void AcceptLocal(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var index = FindIndex(name);
        if (index == 0)
        {
            throw new NetlinkException("the DNS interface was not found", Marshal.GetLastPInvokeError());
        }

        using var socket = new NetlinkSocket(0);
        socket.Request(Request(index, socket.NextSequence()));
    }

    /// <summary>
    /// Builds the interface policy request.
    /// </summary>
    public static byte[] Request(uint index, uint sequence)
    {
        ArgumentOutOfRangeException.ThrowIfZero(index);
        var request = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(request, (uint)request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), SetLink);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), RequestAck);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(20), index);
        Attribute(request.AsSpan(32), 20, AddressFamilies | Nested);
        Attribute(request.AsSpan(36), 16, Inet | Nested);
        Attribute(request.AsSpan(40), 12, InetConfiguration | Nested);
        Attribute(request.AsSpan(44), 8, AcceptLocalAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(48), 1);
        return request;
    }

    private static void Attribute(Span<byte> into, ushort length, int type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(into, length);
        BinaryPrimitives.WriteUInt16LittleEndian(into[2..], (ushort)type);
    }

    [LibraryImport("libc", EntryPoint = "if_nametoindex", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint FindIndex(string name);
}
