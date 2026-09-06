using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// A generic netlink socket to the kernel.
/// </summary>
public sealed partial class NetlinkSocket : IDisposable
{
    private const int AfNetlink = 16;
    private const int SockRaw = 3;
    private const int SockCloexec = 0x80000;
    private const int NetlinkGeneric = 16;

    private const ushort MessageError = 2;
    private const ushort MessageDone = 3;
    private const ushort FlagMulti = 2;

    private const int ReceiveBufferLength = 1 << 17;

    private readonly int _handle;
    private uint _sequence;
    private bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public NetlinkSocket()
    {
        _handle = OpenSocket(AfNetlink, SockRaw | SockCloexec, NetlinkGeneric);
        if (_handle < 0)
        {
            throw new NetlinkException("the netlink socket could not be opened", Marshal.GetLastPInvokeError());
        }

        var address = new SocketAddress { Family = AfNetlink };
        if (BindSocket(_handle, ref address, (uint)Marshal.SizeOf<SocketAddress>()) < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            CloseSocket(_handle);
            throw new NetlinkException("the netlink socket could not be bound", error);
        }
    }

    /// <summary>
    /// Hands out the next sequence number.
    /// </summary>
    public uint NextSequence() => ++_sequence;

    /// <summary>
    /// Sends a request and gathers every message of the answer.
    /// </summary>
    public List<NetlinkMessage> Request(byte[] request)
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        if (SendBytes(_handle, request, (nuint)request.Length, 0) < 0)
        {
            throw new NetlinkException("the netlink request could not be sent", Marshal.GetLastPInvokeError());
        }

        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8));
        var answer = new List<NetlinkMessage>();
        var buffer = new byte[ReceiveBufferLength];
        var reading = true;

        while (reading)
        {
            var read = ReceiveBytes(_handle, buffer, (nuint)buffer.Length, 0);
            if (read < 0)
            {
                throw new NetlinkException("the netlink answer could not be read", Marshal.GetLastPInvokeError());
            }

            reading = Gather(buffer.AsSpan(0, (int)read), sequence, answer);
        }

        return answer;
    }

    /// <summary>
    /// Closes the socket.
    /// </summary>
    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        CloseSocket(_handle);
    }

    private static bool Gather(ReadOnlySpan<byte> buffer, uint sequence, List<NetlinkMessage> answer)
    {
        var offset = 0;
        var more = false;

        while (offset + 16 <= buffer.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(buffer[(offset + 4)..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer[(offset + 6)..]);
            var seen = BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 8)..]);
            if (length < 16 || offset + length > buffer.Length)
            {
                break;
            }

            if (seen != sequence)
            {
                offset += (length + 3) & ~3;
                more = true;

                continue;
            }

            var body = buffer.Slice(offset + 16, length - 16);

            if (type == MessageError)
            {
                var error = BinaryPrimitives.ReadInt32LittleEndian(body);
                if (error != 0)
                {
                    throw new NetlinkException("the kernel refused the netlink request", -error);
                }

                return false;
            }

            if (type == MessageDone)
            {
                return false;
            }

            answer.Add(new NetlinkMessage(type, flags, body.Length > 0 ? body[0] : (byte)0,
                body.Length > 4 ? body[4..].ToArray() : ReadOnlyMemory<byte>.Empty));

            more = (flags & FlagMulti) != 0;
            offset += (length + 3) & ~3;
        }

        return more;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SocketAddress
    {
        public ushort Family;
        public ushort Pad;
        public uint Pid;
        public uint Groups;
    }

    [LibraryImport("libc", EntryPoint = "socket", SetLastError = true)]
    private static partial int OpenSocket(int domain, int type, int protocol);

    [LibraryImport("libc", EntryPoint = "bind", SetLastError = true)]
    private static partial int BindSocket(int handle, ref SocketAddress address, uint length);

    [LibraryImport("libc", EntryPoint = "send", SetLastError = true)]
    private static partial nint SendBytes(int handle, byte[] buffer, nuint length, int flags);

    [LibraryImport("libc", EntryPoint = "recv", SetLastError = true)]
    private static partial nint ReceiveBytes(int handle, byte[] buffer, nuint length, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseSocket(int handle);
}
