using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AmneziaGeo.Server.Awg.Netlink;

/// <summary>
/// One packet the firewall holds until it is told what to do with it.
/// </summary>
/// <param name="Id">The number the kernel knows the packet by.</param>
/// <param name="Mark">The mark the packet carries.</param>
/// <param name="Inbound">The index of the interface the packet came in on, 0 when unknown.</param>
/// <param name="Payload">The packet from its network header on.</param>
public sealed record QueuedPacket(uint Id, uint Mark, uint Inbound, byte[] Payload);

/// <summary>
/// A netlink socket that takes the packets the firewall puts into one queue and gives the verdicts back.
/// </summary>
public sealed partial class NetfilterQueue : IDisposable
{
    /// <summary>
    /// The verdict that drops a packet.
    /// </summary>
    public const uint Drop = 0;

    /// <summary>
    /// The verdict that lets a packet go on.
    /// </summary>
    public const uint Accept = 1;

    /// <summary>
    /// The verdict that sends a packet through the chain it was taken from once more.
    /// </summary>
    public const uint Repeat = 4;

    private const int AfNetlink = 16;
    private const int SockRaw = 3;
    private const int SockCloexec = 0x80000;
    private const int NetlinkNetfilter = 12;

    private const int SocketLevel = 1;
    private const int ReceiveBuffer = 8;
    private const int ReceiveBufferForce = 33;
    private const int ReceiveTimeout = 20;
    private const short PollIn = 1;

    private const int Again = 11;
    private const int Interrupted = 4;
    private const int Overrun = 105;

    private const ushort MessageError = 2;
    private const ushort FlagRequest = 1;
    private const ushort FlagAck = 4;

    private const ushort QueuePacket = 0x0300;
    private const ushort QueueVerdict = 0x0301;
    private const ushort QueueConfig = 0x0302;

    private const ushort ConfigCommand = 1;
    private const ushort ConfigParams = 2;
    private const ushort ConfigLength = 3;
    private const ushort ConfigMask = 4;
    private const ushort ConfigFlags = 5;

    private const byte CommandBind = 1;
    private const byte CopyPacket = 2;
    private const uint FailOpen = 1;

    private const ushort AttributeHead = 1;
    private const ushort AttributeVerdict = 2;
    private const ushort AttributeMark = 3;
    private const ushort AttributeInbound = 5;
    private const ushort AttributePayload = 10;

    private const int AckTries = 8;

    private readonly int _handle;
    private readonly ushort _queue;
    private long _overruns;
    private bool _closed;

    /// <summary>
    /// ctor
    /// </summary>
    public NetfilterQueue(ushort queue, int range, int length, int buffer, TimeSpan wait)
    {
        _queue = queue;
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
            Take(range, length);
        }
        catch
        {
            CloseSocket(_handle);
            _closed = true;
            throw;
        }
    }

    /// <summary>
    /// How many times the kernel had no room to hand a packet over.
    /// </summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    /// <summary>
    /// Reads one batch of packets into the buffer, returning its length or 0 when nothing came in time.
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

        throw new NetlinkException("the queued packets could not be read", error);
    }

    /// <summary>
    /// Tells the kernel what to do with a packet, putting a mark on it when one is given.
    /// </summary>
    public void Decide(uint id, uint verdict, uint? mark = null)
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        var message = Verdict(_queue, id, verdict, mark);
        if (SendBytes(_handle, message, (nuint)message.Length, 0) < 0)
        {
            throw new NetlinkException("the verdict could not be sent", Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>
    /// Waits as long as it takes for a packet or an answer of the kernel to read.
    /// </summary>
    public void Wait()
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        var watched = new Watched { Handle = _handle, Events = PollIn };
        if (PollSockets(ref watched, 1, -1) >= 0)
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != Interrupted)
        {
            throw new NetlinkException("the queued packets could not be waited for", error);
        }
    }

    /// <summary>
    /// Has the kernel answer the socket, which ends a wait for something to read.
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
    /// Adds the packets a batch carries to a list.
    /// </summary>
    public static void Parse(ReadOnlySpan<byte> batch, List<QueuedPacket> into)
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

            if (type == QueuePacket && length >= 20)
            {
                var attributes = NetlinkAttributes.Map(batch.Slice(offset + 20, length - 20).ToArray());
                if (attributes.TryGetValue(AttributeHead, out var head) && head.Length >= 4)
                {
                    into.Add(new QueuedPacket(
                        BinaryPrimitives.ReadUInt32BigEndian(head.Span),
                        Big32(attributes, AttributeMark),
                        Big32(attributes, AttributeInbound),
                        attributes.TryGetValue(AttributePayload, out var payload) ? payload.ToArray() : []));
                }
            }

            offset += (length + 3) & ~3;
        }
    }

    /// <summary>
    /// Returns the request that takes a queue: how much of a packet it copies, how many packets it holds, and that
    /// the packets go on when the queue has no room.
    /// </summary>
    public static byte[] Config(uint sequence, ushort queue, int range, int length)
    {
        var request = new byte[64];
        var span = request.AsSpan();
        Head(span, QueueConfig, FlagRequest | FlagAck, sequence, queue);

        Attribute(span[20..], ConfigCommand, 8);
        span[24] = CommandBind;

        Attribute(span[28..], ConfigParams, 9);
        BinaryPrimitives.WriteUInt32BigEndian(span[32..], (uint)range);
        span[36] = CopyPacket;

        Attribute(span[40..], ConfigLength, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[44..], (uint)length);

        Attribute(span[48..], ConfigMask, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[52..], FailOpen);

        Attribute(span[56..], ConfigFlags, 8);
        BinaryPrimitives.WriteUInt32BigEndian(span[60..], FailOpen);

        return request;
    }

    /// <summary>
    /// Returns the message that gives the verdict on a packet, with the mark to put on it when one is given.
    /// </summary>
    public static byte[] Verdict(ushort queue, uint id, uint verdict, uint? mark)
    {
        var message = new byte[mark is null ? 32 : 40];
        var span = message.AsSpan();
        Head(span, QueueVerdict, FlagRequest, 0, queue);

        Attribute(span[20..], AttributeVerdict, 12);
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], verdict);
        BinaryPrimitives.WriteUInt32BigEndian(span[28..], id);

        if (mark is { } value)
        {
            Attribute(span[32..], AttributeMark, 8);
            BinaryPrimitives.WriteUInt32BigEndian(span[36..], value);
        }

        return message;
    }

    /// <summary>
    /// Closes the socket, which lets the queue go.
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

    private static void Head(Span<byte> at, ushort type, ushort flags, uint sequence, ushort queue)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)at.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(at[4..], type);
        BinaryPrimitives.WriteUInt16LittleEndian(at[6..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(at[8..], sequence);
        BinaryPrimitives.WriteUInt16BigEndian(at[18..], queue);
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

    private void Take(int range, int length)
    {
        const uint sequence = 1;
        var request = Config(sequence, _queue, range, length);
        if (SendBytes(_handle, request, (nuint)request.Length, 0) < 0)
        {
            throw new NetlinkException("the queue could not be asked for", Marshal.GetLastPInvokeError());
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

                throw new NetlinkException("the answer about the queue could not be read", error);
            }

            if (Acked(buffer.AsSpan(0, (int)read), sequence) is { } code)
            {
                if (code != 0)
                {
                    throw new NetlinkException("the kernel refused the queue", code);
                }

                return;
            }
        }

        throw new NetlinkException("the kernel did not answer for the queue", Again);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Watched
    {
        public int Handle;
        public short Events;
        public short Returned;
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

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int PollSockets(ref Watched watched, nuint count, int timeout);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseSocket(int handle);
}
