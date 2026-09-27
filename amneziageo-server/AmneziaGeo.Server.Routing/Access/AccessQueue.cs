using System.Threading.Channels;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// One packet the firewall logged, with the time the panel took it.
/// </summary>
/// <param name="At">When the packet was read off the socket.</param>
/// <param name="Packet">The packet as the kernel handed it over.</param>
public sealed record AccessCatch(DateTimeOffset At, NetfilterPacket Packet);

/// <summary>
/// Hands the logged packets from the reading thread to the writer without ever waiting.
/// </summary>
public sealed class AccessQueue
{
    private readonly Channel<AccessCatch> _channel;

    private long _dropped;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessQueue(int capacity = AccessDefaults.Queue) =>
        _channel = Channel.CreateBounded<AccessCatch>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

    /// <summary>
    /// How many packets were dropped because the writer lagged behind.
    /// </summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>
    /// The side the writer takes the packets from.
    /// </summary>
    public ChannelReader<AccessCatch> Reader => _channel.Reader;

    /// <summary>
    /// Puts a packet in line, dropping and counting it when the line is full.
    /// </summary>
    public bool Offer(AccessCatch item)
    {
        if (_channel.Writer.TryWrite(item))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);

        return false;
    }

    /// <summary>
    /// Counts records lost on the way to the queue.
    /// </summary>
    public void Lose(long count)
    {
        if (count > 0)
        {
            Interlocked.Add(ref _dropped, count);
        }
    }
}
