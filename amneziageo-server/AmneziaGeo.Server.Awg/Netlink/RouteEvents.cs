using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// Hears the kernel tell of changes of the interfaces and of the routing rules of the host.
/// </summary>
public sealed partial class RouteEvents : IDisposable
{
    private const int AfNetlink = 16;
    private const int SockRaw = 3;
    private const int SockCloexec = 0x80000;
    private const int NetlinkRoute = 0;

    private const uint Links = 1;
    private const uint RulesV4 = 1u << 7;
    private const uint RulesV6 = 1u << 18;

    private const int Again = 11;
    private const int Interrupted = 4;
    private const int Overrun = 105;

    private const ushort NewLink = 16;
    private const ushort DelLink = 17;
    private const ushort NewRule = 32;
    private const ushort DelRule = 33;

    private readonly int _handle;
    private bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public RouteEvents()
    {
        _handle = OpenSocket(AfNetlink, SockRaw | SockCloexec, NetlinkRoute);
        if (_handle < 0)
        {
            throw new NetlinkException("the routing socket could not be opened", Marshal.GetLastPInvokeError());
        }

        var address = new SocketAddress { Family = AfNetlink, Groups = Links | RulesV4 | RulesV6 };
        if (BindSocket(_handle, ref address, (uint)Marshal.SizeOf<SocketAddress>()) < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            CloseSocket(_handle);
            _closed = true;
            throw new NetlinkException("the routing socket could not join the groups", error);
        }
    }

    /// <summary>
    /// Waits for news of the kernel: true when it told of a change or dropped news it had no room for, false when the
    /// wait was cut short.
    /// </summary>
    public bool Wait(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_closed, this);

        var read = ReceiveBytes(_handle, buffer, (nuint)buffer.Length, 0);
        if (read >= 0)
        {
            return Told(buffer.AsSpan(0, (int)read));
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == Overrun)
        {
            return true;
        }

        if (error is Again or Interrupted)
        {
            return false;
        }

        throw new NetlinkException("the news of the routing could not be read", error);
    }

    /// <summary>
    /// Has the kernel answer the socket, which ends a wait.
    /// </summary>
    public void Wake()
    {
        if (_closed)
        {
            return;
        }

        var knock = NetlinkSocket.Knock();
        _ = SendBytes(_handle, knock, (nuint)knock.Length, 0);
    }

    /// <summary>
    /// Tells whether a batch carries a change of an interface or of a rule, rather than an answer to a knock.
    /// </summary>
    public static bool Told(ReadOnlySpan<byte> batch)
    {
        var offset = 0;
        while (offset + 16 <= batch.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(batch[offset..]);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(batch[(offset + 4)..]);
            if (length < 16 || offset + length > batch.Length)
            {
                return false;
            }

            if (type is NewLink or DelLink or NewRule or DelRule)
            {
                return true;
            }

            offset += (length + 3) & ~3;
        }

        return false;
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
