using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// One packet the firewall logged to a group.
/// </summary>
/// <param name="Family">The family of the packet: 2 for IPv4, 10 for IPv6.</param>
/// <param name="Prefix">The prefix of the rule that logged it.</param>
/// <param name="Mark">The mark the packet carried when it was logged.</param>
/// <param name="Inbound">The index of the interface the packet came in on, 0 when unknown.</param>
/// <param name="Payload">The head of the packet from its network header on.</param>
public sealed record NetfilterPacket(byte Family, string Prefix, uint Mark, uint Inbound, byte[] Payload);

/// <summary>
/// A netlink socket that takes the packets the firewall logs to one group.
/// </summary>
public sealed partial class NetfilterLog : IDisposable
{
    /// <summary>
    /// The error the kernel answers with when another program holds the group.
    /// </summary>
    public const int Busy = 16;

    /// <summary>
    /// The error the kernel answers with when it cannot log packets to programs.
    /// </summary>
    public const int Unsupported = 22;

    private const int AfNetlink = 16;
    private const int SockRaw = 3;
    private const int SockCloexec = 0x80000;
    private const int NetlinkNetfilter = 12;

    private const int SocketLevel = 1;
    private const int ReceiveBuffer = 8;
    private const int ReceiveBufferForce = 33;
    private const int ReceiveTimeout = 20;

    private const int Again = 11;
    private const int Interrupted = 4;
    private const int Overrun = 105;

    private const ushort MessageError = 2;
    private const ushort FlagRequest = 1;
    private const ushort FlagAck = 4;

    private const ushort LogPacket = 0x0400;
    private const ushort LogConfig = 0x0401;

    private const ushort ConfigCommand = 1;
    private const ushort ConfigMode = 2;
    private const ushort ConfigBuffer = 3;
    private const ushort ConfigTimeout = 4;
    private const ushort ConfigThreshold = 5;

    private const byte CommandBind = 1;
    private const byte CopyPacket = 2;

    private const ushort AttributeMark = 2;
    private const ushort AttributeInbound = 4;
    private const ushort AttributePayload = 9;
    private const ushort AttributePrefix = 10;

    private const int KernelBuffer = 65536;
    private const int FlushHundredths = 10;
    private const int Threshold = 64;
    private const int AckTries = 8;

    private readonly int _handle;
    private long _overruns;
    private bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public NetfilterLog(ushort group, int snap, int buffer, TimeSpan wait)
    {
        _handle = OpenSocket(AfNetlink, SockRaw | SockCloexec, NetlinkNetfilter);
        if (_handle < 0)
        {
            throw new NetlinkException("the netfilter socket could not be opened", Marshal.GetLastPInvokeError());
        }

        try
        {
            var address = new SocketAddress { Family = AfNetlink };
            if (BindSocket(_handle, ref address, (uint)Marshal.SizeOf<SocketAddress>()) < 0)
            {
                throw new NetlinkException("the netfilter socket could not be bound", Marshal.GetLastPInvokeError());
            }

            Room(buffer);
            Patience(wait);
            Take(group, snap);
        }
        catch
        {
            CloseSocket(_handle);
            _closed = true;
            throw;
        }
    }

    /// <summary>
    /// How many times the kernel dropped records the socket had no room for.
    /// </summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    /// <summary>
    /// Reads one batch of records into the buffer, returning its length or 0 when nothing came in time.
    /// </summary>
    public int Read(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_closed, this);

        var read = ReceiveBytes(_handle, buffer, (nuint)buffer.Length, 0);
        if (read >= 0)
        {
            return (int)read;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == Overrun)
        {
            Interlocked.Increment(ref _overruns);
            return 0;
        }

        if (error is Again or Interrupted)
        {
            return 0;
        }

        throw new NetlinkException("the netfilter records could not be read", error);
    }

    /// <summary>
    /// Adds the packets a batch of records carries to a list.
    /// </summary>
    public static void Parse(ReadOnlySpan<byte> batch, List<NetfilterPacket> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        var offset = 0;
        while (offset + 16 <= batch.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(batch[offset..]);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(batch[(offset + 4)..]);
            if (length < 16 || offset + length > batch.Length)
            {
                return;
            }

            if (type == LogPacket && length >= 20)
            {
                var family = batch[offset + 16];
                var attributes = NetlinkAttributes.Map(batch.Slice(offset + 20, length - 20).ToArray());
                into.Add(new NetfilterPacket(
                    family,
                    NetlinkAttributes.Text(attributes, AttributePrefix) ?? string.Empty,
                    Big32(attributes, AttributeMark),
                    Big32(attributes, AttributeInbound),
                    attributes.TryGetValue(AttributePayload, out var payload) ? payload.ToArray() : []));
            }

            offset += (length + 3) & ~3;
        }
    }

    /// <summary>
    /// Returns the request that takes a group and sets how much of a packet it copies.
    /// </summary>
    public static byte[] Config(uint sequence, ushort group, int snap)
    {
        var request = new byte[64];
        var span = request.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], LogConfig);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], FlagRequest | FlagAck);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], sequence);
        BinaryPrimitives.WriteUInt16BigEndian(span[18..], group);

        Attribute(span[20..], ConfigCommand, 5);
        span[24] = CommandBind;

        Attribute(span[28..], ConfigMode, 10);
        BinaryPrimitives.WriteUInt32BigEndian(span[32..], (uint)snap);
        span[36] = CopyPacket;

        Attribute(span[40..], ConfigBuffer, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[44..], KernelBuffer);

        Attribute(span[48..], ConfigTimeout, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[52..], FlushHundredths);

        Attribute(span[56..], ConfigThreshold, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[60..], Threshold);

        return request;
    }

    /// <summary>
    /// Closes the socket, which lets the group go.
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

    private static void Attribute(Span<byte> at, ushort type, ushort length)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(at, length);
        BinaryPrimitives.WriteUInt16LittleEndian(at[2..], type);
    }

    private static uint Big32(Dictionary<ushort, ReadOnlyMemory<byte>> map, ushort type) =>
        map.TryGetValue(type, out var value) && value.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(value.Span) : 0;

    private void Room(int buffer)
    {
        var size = BitConverter.GetBytes(buffer);
        if (SetOption(_handle, SocketLevel, ReceiveBufferForce, size, (uint)size.Length) < 0)
        {
            _ = SetOption(_handle, SocketLevel, ReceiveBuffer, size, (uint)size.Length);
        }
    }

    private void Patience(TimeSpan wait)
    {
        var time = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(time, (long)wait.TotalSeconds);
        BinaryPrimitives.WriteInt64LittleEndian(time.AsSpan(8), wait.Microseconds + (wait.Milliseconds * 1000L));
        if (SetOption(_handle, SocketLevel, ReceiveTimeout, time, (uint)time.Length) < 0)
        {
            throw new NetlinkException("the netfilter socket could not be given a timeout", Marshal.GetLastPInvokeError());
        }
    }

    private void Take(ushort group, int snap)
    {
        const uint sequence = 1;
        var request = Config(sequence, group, snap);
        if (SendBytes(_handle, request, (nuint)request.Length, 0) < 0)
        {
            throw new NetlinkException("the netfilter group could not be asked for", Marshal.GetLastPInvokeError());
        }

        var buffer = new byte[8192];
        for (var tries = 0; tries < AckTries; tries++)
        {
            var read = ReceiveBytes(_handle, buffer, (nuint)buffer.Length, 0);
            if (read < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is Again or Interrupted)
                {
                    continue;
                }

                throw new NetlinkException("the netfilter answer could not be read", error);
            }

            if (Acked(buffer.AsSpan(0, (int)read), sequence) is { } code)
            {
                if (code != 0)
                {
                    throw new NetlinkException("the kernel refused the netfilter group", code);
                }

                return;
            }
        }

        throw new NetlinkException("the kernel did not answer for the netfilter group", Again);
    }

    private static int? Acked(ReadOnlySpan<byte> batch, uint sequence)
    {
        var offset = 0;
        while (offset + 16 <= batch.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(batch[offset..]);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(batch[(offset + 4)..]);
            var seen = BinaryPrimitives.ReadUInt32LittleEndian(batch[(offset + 8)..]);
            if (length < 16 || offset + length > batch.Length)
            {
                return null;
            }

            if (type == MessageError && seen == sequence && length >= 20)
            {
                return -BinaryPrimitives.ReadInt32LittleEndian(batch[(offset + 16)..]);
            }

            offset += (length + 3) & ~3;
        }

        return null;
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

    [LibraryImport("libc", EntryPoint = "setsockopt", SetLastError = true)]
    private static partial int SetOption(int handle, int level, int name, byte[] value, uint length);

    [LibraryImport("libc", EntryPoint = "send", SetLastError = true)]
    private static partial nint SendBytes(int handle, byte[] buffer, nuint length, int flags);

    [LibraryImport("libc", EntryPoint = "recv", SetLastError = true)]
    private static partial nint ReceiveBytes(int handle, byte[] buffer, nuint length, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseSocket(int handle);
}
