using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// Tells the rules whether the panel reads names out of the connections: the queue of the firewall and the port it
/// takes TCP connections on.
/// </summary>
public sealed class NameGate
{
    private int _queue;

    private int _port;

    private int _six;

    private int _door;

    private string? _fault;

    /// <summary>
    /// What the firewall hands over to the panel right now, or null when the panel takes nothing.
    /// </summary>
    public RouteReading? Offer
    {
        get
        {
            var queue = Volatile.Read(ref _queue) == 1 ? NameDefaults.Queue : (ushort?)null;
            var port = Volatile.Read(ref _port);
            var door = Volatile.Read(ref _door);

            return queue is null && port == 0
                ? null
                : new RouteReading(
                    queue,
                    port == 0 ? null : port,
                    port != 0 && Volatile.Read(ref _six) == 1,
                    port == 0 || door == 0 ? null : door);
        }
    }

    /// <summary>
    /// Why the panel reads no names in the queue, or null when it does or was not asked to.
    /// </summary>
    public string? Fault => Volatile.Read(ref _fault);

    /// <summary>
    /// Tells the rules that the panel takes the queue.
    /// </summary>
    public void OpenQueue()
    {
        Volatile.Write(ref _fault, null);
        Volatile.Write(ref _queue, 1);
    }

    /// <summary>
    /// Tells the rules that the panel takes no queue, with the reason when it should.
    /// </summary>
    public void CloseQueue(string? fault = null)
    {
        Volatile.Write(ref _queue, 0);
        Volatile.Write(ref _fault, fault);
    }

    /// <summary>
    /// Tells the rules that the panel takes TCP connections on a port, those of IPv6 too when it says so, and the
    /// port of the host they come in through when there is one.
    /// </summary>
    public void OpenPort(int port, bool six, int? door = null)
    {
        Volatile.Write(ref _six, six ? 1 : 0);
        Volatile.Write(ref _door, door ?? 0);
        Volatile.Write(ref _port, port);
    }

    /// <summary>
    /// Tells the rules that the panel takes no TCP connections.
    /// </summary>
    public void ClosePort() => Volatile.Write(ref _port, 0);
}
