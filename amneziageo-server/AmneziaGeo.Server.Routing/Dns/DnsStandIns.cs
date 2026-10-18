using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// One name the resolver answers with a stand-in address.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Address">The stand-in address the name is answered with.</param>
/// <param name="Real">The address the stand-in leads to, or null when none is known.</param>
/// <param name="Seen">When the name was asked about last.</param>
public sealed record DnsStandIn(string Name, IPAddress Address, IPAddress? Real, DateTimeOffset Seen);

/// <summary>
/// Hands out the stand-in addresses the resolver answers names with and writes for the host where they lead.
/// </summary>
public sealed class DnsStandIns
{
    /// <summary>
    /// The map of the routing table that leads the stand-in addresses to the real ones.
    /// </summary>
    public const string MapName = "stand";

    /// <summary>
    /// How many elements one command carries.
    /// </summary>
    public const int MaxBatch = 512;

    /// <summary>
    /// How long a name nobody asks about keeps its stand-in address.
    /// </summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(7);

    private readonly Lock _sync = new();

    private readonly Dictionary<string, Stand> _names = new(StringComparer.Ordinal);

    private readonly Dictionary<uint, string> _owners = [];

    private readonly Dictionary<uint, IPAddress> _laid = [];

    private readonly TimeProvider _time;

    private readonly uint _first;

    private readonly uint _count;

    private long _stirs;

    private bool _spoiled;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsStandIns(TimeProvider? time = null, string range = TemplateDefaults.StandIns)
    {
        var network = IPNetwork.Parse(range);
        if (network.BaseAddress.AddressFamily != AddressFamily.InterNetwork || network.PrefixLength > 30)
        {
            throw new ArgumentException($"'{range}' is not an IPv4 range of four addresses or more", nameof(range));
        }

        _time = time ?? TimeProvider.System;
        _first = Number(network.BaseAddress) + 1;
        _count = (uint)((1L << (32 - network.PrefixLength)) - 2);
        Range = network.ToString();
    }

    /// <summary>
    /// The range the stand-in addresses come from.
    /// </summary>
    public string Range { get; }

    /// <summary>
    /// How many names hold a stand-in address.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _names.Count;
            }
        }
    }

    /// <summary>
    /// How many times the names or the addresses they lead to changed.
    /// </summary>
    public long Stirs => Interlocked.Read(ref _stirs);

    /// <summary>
    /// Returns the stand-in address of a name, taking one when the name has none, and leads it to one of the real
    /// addresses; tells whether the host has yet to learn where it leads.
    /// </summary>
    public IPAddress Take(string name, IReadOnlyList<IPAddress> real, out bool waiting)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(real);

        var four = real.Where(address => address.AddressFamily == AddressFamily.InterNetwork).ToArray();
        if (four.Length == 0)
        {
            throw new ArgumentException("no IPv4 address to lead to", nameof(real));
        }

        var key = Key(name);
        var now = _time.GetUtcNow();
        lock (_sync)
        {
            if (!_names.TryGetValue(key, out var held))
            {
                held = new Stand(Free(key), null, now);
                _names[key] = held;
                _owners[held.Slot] = key;
                Interlocked.Increment(ref _stirs);
            }

            held.Seen = now;
            if (held.Real is null || !four.Contains(held.Real))
            {
                held.Real = four[0];
                Interlocked.Increment(ref _stirs);
            }

            waiting = !_laid.TryGetValue(held.Slot, out var laid) || !laid.Equals(held.Real);

            return Address(held.Slot);
        }
    }

    /// <summary>
    /// Returns the real address a stand-in leads to, or null when it leads nowhere.
    /// </summary>
    public IPAddress? Real(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_sync)
        {
            return _owners.TryGetValue(Number(address), out var key) ? _names[key].Real : null;
        }
    }

    /// <summary>
    /// Returns the names with their stand-in addresses.
    /// </summary>
    public IReadOnlyList<DnsStandIn> Held()
    {
        lock (_sync)
        {
            return [.. _names.Select(pair => new DnsStandIn(pair.Key, Address(pair.Value.Slot), pair.Value.Real, pair.Value.Seen))];
        }
    }

    /// <summary>
    /// Takes the names the resolver answered with stand-ins before the panel started.
    /// </summary>
    public void Restore(IReadOnlyList<DnsStandIn> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        lock (_sync)
        {
            foreach (var one in held.OrderByDescending(one => one.Seen))
            {
                var key = Key(one.Name);
                var slot = Number(one.Address);
                if (key.Length == 0 || !Inside(one.Address) || _names.ContainsKey(key) || _owners.ContainsKey(slot))
                {
                    continue;
                }

                var real = one.Real is { AddressFamily: AddressFamily.InterNetwork } ? one.Real : null;
                _names[key] = new Stand(slot, real, one.Seen);
                _owners[slot] = key;
            }
        }
    }

    /// <summary>
    /// Lets go the names nobody asked about for longer than they are kept.
    /// </summary>
    public int Sweep()
    {
        var oldest = _time.GetUtcNow() - Kept;
        lock (_sync)
        {
            var gone = _names.Where(pair => pair.Value.Seen < oldest).Select(pair => pair.Key).ToArray();
            foreach (var key in gone)
            {
                Drop(key);
            }

            return gone.Length;
        }
    }

    /// <summary>
    /// Returns the commands that lay every stand-in into a map the host has just made, taking the host as holding them.
    /// </summary>
    public string Lay()
    {
        lock (_sync)
        {
            _laid.Clear();
            _spoiled = false;

            return Adds(Wanted());
        }
    }

    /// <summary>
    /// Takes the host as holding no stand-in.
    /// </summary>
    public void Forget()
    {
        lock (_sync)
        {
            _laid.Clear();
            _spoiled = false;
        }
    }

    /// <summary>
    /// Returns the commands that carry to the host what changed since the last ones, taking the host as holding it.
    /// </summary>
    public string Changes()
    {
        lock (_sync)
        {
            if (_spoiled)
            {
                _laid.Clear();
                _spoiled = false;

                return $"flush map inet {RouteRuleset.TableName} {MapName}\n" + Adds(Wanted());
            }

            var wanted = Wanted();
            var gone = _laid
                .Where(pair => !wanted.TryGetValue(pair.Key, out var real) || !real.Equals(pair.Value))
                .Select(pair => pair.Key)
                .ToHashSet();
            var added = wanted.Where(pair => !_laid.ContainsKey(pair.Key) || gone.Contains(pair.Key)).ToArray();
            var text = new StringBuilder();
            foreach (var batch in gone.Chunk(MaxBatch))
            {
                text.Append("delete element inet ").Append(RouteRuleset.TableName).Append(' ').Append(MapName);
                text.Append(" { ").Append(string.Join(", ", batch.Select(slot => Address(slot).ToString()))).Append(" }\n");
                foreach (var slot in batch)
                {
                    _laid.Remove(slot);
                }
            }

            return text.Append(Adds(added)).ToString();
        }
    }

    /// <summary>
    /// Takes the host as holding something else than the commands left it with, so the next ones lay the map anew.
    /// </summary>
    public void Spoil()
    {
        lock (_sync)
        {
            _spoiled = true;
        }
    }

    // Writes the commands that add the elements and takes the host as holding them.
    private string Adds(IEnumerable<KeyValuePair<uint, IPAddress>> elements)
    {
        var text = new StringBuilder();
        foreach (var batch in elements.Chunk(MaxBatch))
        {
            text.Append("add element inet ").Append(RouteRuleset.TableName).Append(' ').Append(MapName).Append(" { ");
            text.Append(string.Join(", ", batch.Select(pair => $"{Address(pair.Key)} : {pair.Value}"))).Append(" }\n");
            foreach (var pair in batch)
            {
                _laid[pair.Key] = pair.Value;
            }
        }

        return text.ToString();
    }

    private Dictionary<uint, IPAddress> Wanted() =>
        _names.Values.Where(held => held.Real is not null).ToDictionary(held => held.Slot, held => held.Real!);

    // Returns the slot a name lands on: the one its digest points at, or the next free one after it; a full range
    // lets the name asked about longest ago go first.
    private uint Free(string key)
    {
        if (_names.Count >= _count)
        {
            Drop(_names.MinBy(pair => pair.Value.Seen).Key);
        }

        var start = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(key))) % _count;
        for (var step = 0u; step < _count; step++)
        {
            var slot = _first + ((start + step) % _count);
            if (!_owners.ContainsKey(slot))
            {
                return slot;
            }
        }

        throw new InvalidOperationException("the range of the stand-in addresses is full");
    }

    private void Drop(string key)
    {
        if (_names.Remove(key, out var held))
        {
            _owners.Remove(held.Slot);
            Interlocked.Increment(ref _stirs);
        }
    }

    private bool Inside(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetwork && Number(address) - _first < _count;

    private static string Key(string name) => name.Trim().TrimEnd('.').ToLowerInvariant();

    private static uint Number(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.MapToIPv4().GetAddressBytes());

    private static IPAddress Address(uint slot)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, slot);

        return new IPAddress(bytes);
    }

    private sealed class Stand(uint slot, IPAddress? real, DateTimeOffset seen)
    {
        public uint Slot { get; } = slot;

        public IPAddress? Real { get; set; } = real;

        public DateTimeOffset Seen { get; set; } = seen;
    }
}
