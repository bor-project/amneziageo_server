using AmneziaGeo.Server.Api.Dns;
using AmneziaGeo.Server.Routing.Names;

namespace AmneziaGeo.Server.Api.Names;

/// <summary>
/// Tells what the reading of names out of the connections is doing.
/// </summary>
public sealed class NameState
{
    private readonly NameGate _gate;

    private readonly NameDesk _desk;

    private readonly NameQueue _queue;

    private readonly NameRelay _relay;

    /// <summary>
    /// ctor
    /// </summary>
    public NameState(NameGate gate, NameDesk desk, NameQueue queue, NameRelay relay)
    {
        _gate = gate;
        _desk = desk;
        _queue = queue;
        _relay = relay;
    }

    /// <summary>
    /// Why the names are not read in the queue, or null when they are.
    /// </summary>
    public string? Fault => _gate.Fault;

    /// <summary>
    /// What was read since the panel started.
    /// </summary>
    public DnsReadingBody Counts
    {
        get
        {
            var offer = _gate.Offer;

            return new DnsReadingBody(
                offer?.Queue is not null,
                offer?.Port is not null,
                _queue.Read,
                _queue.Named,
                _queue.Reset,
                _queue.Missed,
                _relay.Taken,
                _relay.Open,
                _desk.Learned);
        }
    }
}
