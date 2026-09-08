using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// One address the resolver puts into the set of a rule.
/// </summary>
/// <param name="Rule">The rule the address belongs to.</param>
/// <param name="Address">The address itself.</param>
public sealed record DnsEntry(long Rule, IPAddress Address)
{
    /// <summary>
    /// Tells whether the address is an IPv6 one.
    /// </summary>
    public bool IsSix => Address.AddressFamily == AddressFamily.InterNetworkV6;
}

/// <summary>
/// Puts the answered addresses into the sets of the rules, in batches.
/// </summary>
public sealed class DnsSets
{
    /// <summary>
    /// How many addresses go to the host in one command.
    /// </summary>
    public const int MaxBatch = 512;

    private readonly ConcurrentQueue<DnsEntry> _waiting = new();

    private readonly ConcurrentDictionary<string, DateTimeOffset> _held = new(StringComparer.Ordinal);

    private readonly IHostNetwork _network;

    private readonly TimeProvider _time;

    private long _added;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsSets(IHostNetwork network, TimeProvider? time = null)
    {
        _network = network;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// How many addresses wait to go to the host.
    /// </summary>
    public int Waiting => _waiting.Count;

    /// <summary>
    /// How many addresses went into the sets.
    /// </summary>
    public long Added => Interlocked.Read(ref _added);

    /// <summary>
    /// Takes an address the resolver answered with.
    /// </summary>
    public void Add(long rule, IPAddress address, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(address);

        var key = rule.ToString(CultureInfo.InvariantCulture) + "|" + address;
        var now = _time.GetUtcNow();
        if (_held.TryGetValue(key, out var until) && until > now)
        {
            return;
        }

        _held[key] = now + (lifetime > TimeSpan.Zero ? lifetime / 2 : TimeSpan.Zero);
        _waiting.Enqueue(new DnsEntry(rule, address));
    }

    /// <summary>
    /// Sends the addresses that wait into the sets of the rules the host carries.
    /// </summary>
    public async Task<int> FlushAsync(RoutePlan? plan, TimeSpan lifetime, CancellationToken ct)
    {
        var live = Live(plan);
        var batch = new List<DnsEntry>(MaxBatch);
        while (batch.Count < MaxBatch && _waiting.TryDequeue(out var entry))
        {
            if (live.Contains(entry.Rule))
            {
                batch.Add(entry);
            }
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        await _network.FirewallAsync(Text(batch, lifetime), ct).ConfigureAwait(false);
        Interlocked.Add(ref _added, batch.Count);

        return batch.Count;
    }

    /// <summary>
    /// Drops what waits and what is known to be on the host.
    /// </summary>
    public void Forget()
    {
        _held.Clear();
        while (_waiting.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Returns the commands that put the addresses into the sets.
    /// </summary>
    public static string Text(IReadOnlyList<DnsEntry> entries, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var seconds = (int)Math.Max(1, lifetime.TotalSeconds);
        var text = new StringBuilder();
        foreach (var group in entries.GroupBy(entry => (entry.Rule, entry.IsSix)))
        {
            var elements = group
                .Select(entry => entry.Address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(address => $"{address} timeout {seconds}s");

            text.Append("add element inet ").Append(RouteRuleset.TableName).Append(' ');
            text.Append(RouteRuleset.NameSet(group.Key.Rule, group.Key.IsSix));
            text.Append(" { ").Append(string.Join(", ", elements)).Append(" }\n");
        }

        return text.ToString();
    }

    private static HashSet<long> Live(RoutePlan? plan) =>
        plan is null
            ? []
            : [.. plan.Legs.Where(leg => leg.IsLive && leg.Domains.Count > 0).Select(leg => leg.Rule.Id)];
}
