using System.Net;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Guard;

/// <summary>
/// A peer caught carrying two devices: the address it keeps and the ones it cuts off.
/// </summary>
/// <param name="Interface">The interface the peer belongs to.</param>
/// <param name="Peer">The peer as the kernel held it when it was caught.</param>
/// <param name="First">The address the peer keeps.</param>
/// <param name="Cut">The addresses the peer is cut off at.</param>
public sealed record GuardCut(string Interface, AwgPeer Peer, IPEndPoint First, IReadOnlyList<IPEndPoint> Cut);

/// <summary>
/// One address the firewall holds off the port of an interface.
/// </summary>
/// <param name="Source">The address and the port the packets come from.</param>
/// <param name="Port">The port of the interface they go to.</param>
public sealed record GuardHold(IPEndPoint Source, ushort Port);

/// <summary>
/// Catches the peers whose address comes back to a device that is still sending and keeps the second devices cut off.
/// </summary>
public sealed class PeerGuard
{
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);

    /// <summary>
    /// Takes the peers an interface carries now and returns the ones just caught carrying a second device, a device
    /// counting as gone once it stays silent longer than the quiet span.
    /// </summary>
    public IReadOnlyList<GuardCut> Observe(AwgDevice device, TimeSpan quiet, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(device);

        var caught = new List<GuardCut>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var peer in device.Peers)
        {
            var key = Key(device.Name, peer.PublicKey);
            present.Add(key);
            if (!_tracks.TryGetValue(key, out var track))
            {
                track = new Track { Rx = peer.RxBytes, HeardAt = peer.LastHandshake };
                _tracks[key] = track;
            }

            if (Step(device, peer, track, quiet, now) is { } cut)
            {
                caught.Add(cut);
            }
        }

        var prefix = device.Name + "/";
        var gone = _tracks.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal) && !present.Contains(key))
            .ToArray();
        foreach (var key in gone)
        {
            _tracks.Remove(key);
        }

        return caught;
    }

    /// <summary>
    /// Returns the addresses the firewall is to hold off the ports of the interfaces.
    /// </summary>
    public IReadOnlyList<GuardHold> Holds() =>
    [
        .. _tracks.Values
            .SelectMany(track => track.Cut.Select(point => new GuardHold(point, track.Port)))
            .Distinct()
    ];

    /// <summary>
    /// Returns the addresses a peer of an interface is kept cut off at.
    /// </summary>
    public IReadOnlyList<string> Cut(string name, string publicKey) =>
        _tracks.TryGetValue(Key(name, publicKey), out var track) ? [.. track.Cut.Select(point => point.ToString())] : [];

    /// <summary>
    /// Returns when a peer of an interface last sent anything, null when the guard has not heard it.
    /// </summary>
    public DateTimeOffset? Heard(string name, string publicKey) =>
        _tracks.TryGetValue(Key(name, publicKey), out var track) ? track.HeardAt : null;

    private static GuardCut? Step(AwgDevice device, AwgPeer peer, Track track, TimeSpan quiet, DateTimeOffset now)
    {
        track.Port = device.ListenPort;
        if (peer.RxBytes != track.Rx)
        {
            track.Rx = peer.RxBytes;
            track.HeardAt = now;
        }

        if (track.Cut.Count > 0 && Silent(track, quiet, now))
        {
            track.Cut.Clear();
            track.Changes.Clear();
        }

        if (Plain(peer.Endpoint) is { } point && (track.Changes.Count == 0 || !track.Changes[^1].Point.Equals(point)))
        {
            track.Changes.Add(new Change(now, point));
        }

        while (track.Changes.Count > 1 && now - track.Changes[1].At > quiet)
        {
            track.Changes.RemoveAt(0);
        }

        if (Returned(track.Changes) is not { } flap)
        {
            return null;
        }

        var fresh = flap.Rest.Where(point => !track.Cut.Contains(point)).ToArray();
        if (fresh.Length == 0)
        {
            return null;
        }

        track.Cut.Remove(flap.First);
        track.Cut.AddRange(fresh);
        track.Changes.Clear();
        track.Changes.Add(new Change(now, flap.First));

        return new GuardCut(device.Name, peer, flap.First, [.. track.Cut]);
    }

    private static Flap? Returned(IReadOnlyList<Change> changes)
    {
        if (changes.Count < 3)
        {
            return null;
        }

        var back = changes[^1].Point;
        if (!changes.Take(changes.Count - 2).Any(change => change.Point.Equals(back)))
        {
            return null;
        }

        var rest = changes.Select(change => change.Point).Where(point => !point.Equals(back)).Distinct().ToArray();

        return rest.Length == 0 ? null : new Flap(back, rest);
    }

    private static bool Silent(Track track, TimeSpan quiet, DateTimeOffset now) =>
        track.HeardAt is not { } heard || now - heard > quiet;

    private static IPEndPoint? Plain(IPEndPoint? point)
    {
        if (point is null || !point.Address.IsIPv4MappedToIPv6)
        {
            return point;
        }

        return new IPEndPoint(point.Address.MapToIPv4(), point.Port);
    }

    private static string Key(string name, string publicKey) => name + "/" + publicKey;

    private sealed record Change(DateTimeOffset At, IPEndPoint Point);

    private sealed record Flap(IPEndPoint First, IReadOnlyList<IPEndPoint> Rest);

    private sealed class Track
    {
        public ushort Port { get; set; }

        public ulong Rx { get; set; }

        public DateTimeOffset? HeardAt { get; set; }

        public List<Change> Changes { get; } = [];

        public List<IPEndPoint> Cut { get; } = [];
    }
}
