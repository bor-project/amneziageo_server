using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
/// One address a rule stands on until newer ones push it out.
/// </summary>
/// <param name="Rule">The rule the address belongs to.</param>
/// <param name="Address">The address itself.</param>
/// <param name="Seen">When the address was answered last.</param>
public sealed record DnsStanding(long Rule, IPAddress Address, DateTimeOffset Seen);

/// <summary>
/// Puts the answered addresses into the sets of the rules, in batches.
/// </summary>
public sealed class DnsSets
{
    /// <summary>
    /// How many addresses go to the host in one command.
    /// </summary>
    public const int MaxBatch = 512;

    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<DnsEntry> _waiting = new();

    private readonly ConcurrentDictionary<string, Held> _held = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly IHostNetwork _network;

    private readonly TimeProvider _time;

    private readonly int _standing;

    private long _added;

    private long _stirs;

    private long _life = TimeSpan.FromMinutes(DnsDefaults.NameMinutes).Ticks;

    private DateTimeOffset _swept;

    private bool _seeded;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsSets(IHostNetwork network, TimeProvider? time = null, int standing = DnsDefaults.StandingAddresses)
    {
        _network = network;
        _time = time ?? TimeProvider.System;
        _standing = standing > 0 ? standing : 0;
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
    /// How many times the addresses the rules stand on changed.
    /// </summary>
    public long Stirs => Interlocked.Read(ref _stirs);

    private TimeSpan Lifetime => TimeSpan.FromTicks(Interlocked.Read(ref _life));

    /// <summary>
    /// Takes an address the resolver answered with, telling whether it has to go to the host.
    /// </summary>
    public bool Add(long rule, IPAddress address, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(address);

        var key = Key(rule, address);
        var now = _time.GetUtcNow();
        if (_held.TryGetValue(key, out var held))
        {
            if (held.Again > now)
            {
                return false;
            }
        }
        else
        {
            Interlocked.Increment(ref _stirs);
        }

        Note(lifetime);
        var entry = new DnsEntry(rule, address);
        _held[key] = new Held(entry, now + (lifetime > TimeSpan.Zero ? lifetime / 2 : TimeSpan.Zero), now + lifetime, now);
        _waiting.Enqueue(entry);

        return true;
    }

    /// <summary>
    /// Tells whether an address sits in the set of a rule and its time has not run out.
    /// </summary>
    public bool Holds(long rule, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return _held.TryGetValue(Key(rule, address), out var held) && held.Until > _time.GetUtcNow();
    }

    /// <summary>
    /// Takes the addresses the rules stood on before the panel started.
    /// </summary>
    public void Restore(IReadOnlyList<DnsStanding> standings, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(standings);

        Note(lifetime);
        var now = _time.GetUtcNow();
        var life = Lifetime;
        foreach (var standing in standings)
        {
            var entry = new DnsEntry(standing.Rule, standing.Address);
            var held = new Held(entry, now + life / 2, now + life, standing.Seen);
            if (_held.TryAdd(Key(standing.Rule, standing.Address), held))
            {
                _waiting.Enqueue(entry);
            }
        }
    }

    /// <summary>
    /// Returns the addresses the rules of a plan stand on.
    /// </summary>
    public IReadOnlyList<DnsStanding> Standings(RoutePlan? plan) =>
        [.. Standing(Live(plan)).Values.Select(held => new DnsStanding(held.Entry.Rule, held.Entry.Address, held.Seen))];

    /// <summary>
    /// Sends the addresses that wait into the sets of the rules the host carries.
    /// </summary>
    public async Task<int> FlushAsync(RoutePlan? plan, TimeSpan lifetime, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Note(lifetime);
            Renew(plan);
            if (_waiting.IsEmpty)
            {
                return 0;
            }

            var taken = Take(Live(plan));
            foreach (var batch in taken.Chunk(MaxBatch))
            {
                await _network.FirewallAsync(Text(batch, lifetime), ct).ConfigureAwait(false);
                Interlocked.Add(ref _added, batch.Length);
            }

            return taken.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Puts a ruleset on the host together with the addresses whose time has not run out.
    /// </summary>
    public async Task<int> LayAsync(string ruleset, RoutePlan? plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_seeded)
            {
                Seed(await _network.ReadFirewallAsync(RouteRuleset.TableName, ct).ConfigureAwait(false));
                _seeded = true;
            }

            var kept = Kept(plan);
            await _network.FirewallAsync(ruleset + Write(kept), ct).ConfigureAwait(false);

            return kept.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns the commands that put the addresses into the sets.
    /// </summary>
    public static string Text(IReadOnlyList<DnsEntry> entries, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var seconds = Seconds(lifetime);

        return Write([.. entries.Select(entry => (entry, seconds))]);
    }

    private static string Write(IReadOnlyList<(DnsEntry Entry, int Seconds)> items)
    {
        var text = new StringBuilder();
        foreach (var group in items.GroupBy(item => (item.Entry.Rule, item.Entry.IsSix)))
        {
            var elements = group
                .GroupBy(item => item.Entry.Address.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(same => $"{same.Key} timeout {same.Max(item => item.Seconds)}s");

            text.Append("add element inet ").Append(RouteRuleset.TableName).Append(' ');
            text.Append(RouteRuleset.NameSet(group.Key.Rule, group.Key.IsSix));
            text.Append(" { ").Append(string.Join(", ", elements)).Append(" }\n");
        }

        return text.ToString();
    }

    private static int Seconds(TimeSpan lifetime) => (int)Math.Max(1, Math.Ceiling(lifetime.TotalSeconds));

    private List<DnsEntry> Take(HashSet<long> live)
    {
        var taken = new List<DnsEntry>();
        var left = _waiting.Count;
        while (left-- > 0 && _waiting.TryDequeue(out var entry))
        {
            if (live.Contains(entry.Rule))
            {
                taken.Add(entry);
            }
        }

        return taken;
    }

    private static string Key(long rule, IPAddress address) =>
        rule.ToString(CultureInfo.InvariantCulture) + "|" + address;

    private List<(DnsEntry Entry, int Seconds)> Kept(RoutePlan? plan)
    {
        var now = _time.GetUtcNow();
        var live = Live(plan);
        var standing = Standing(live);
        var life = Lifetime;
        var kept = new List<(DnsEntry Entry, int Seconds)>();
        foreach (var pair in _held)
        {
            if (standing.ContainsKey(pair.Key))
            {
                _held[pair.Key] = pair.Value with { Again = now + life / 2, Until = now + life };
                kept.Add((pair.Value.Entry, Seconds(life)));
            }
            else if (pair.Value.Until <= now)
            {
                Drop(pair.Key);
            }
            else if (live.Contains(pair.Value.Entry.Rule))
            {
                kept.Add((pair.Value.Entry, Seconds(pair.Value.Until - now)));
            }
        }

        return kept;
    }

    private void Renew(RoutePlan? plan)
    {
        var now = _time.GetUtcNow();
        if (now < _swept)
        {
            return;
        }

        _swept = now + Sweep;
        var life = Lifetime;
        foreach (var pair in Standing(Live(plan)))
        {
            if (pair.Value.Again > now)
            {
                continue;
            }

            _held[pair.Key] = pair.Value with { Again = now + life / 2, Until = now + life };
            _waiting.Enqueue(pair.Value.Entry);
        }
    }

    private Dictionary<string, Held> Standing(HashSet<long> live)
    {
        if (_standing == 0)
        {
            return [];
        }

        return _held
            .Where(pair => live.Contains(pair.Value.Entry.Rule))
            .GroupBy(pair => pair.Value.Entry.Rule)
            .SelectMany(group => group.OrderByDescending(pair => pair.Value.Seen).Take(_standing))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private void Drop(string key)
    {
        if (_held.TryRemove(key, out _))
        {
            Interlocked.Increment(ref _stirs);
        }
    }

    private void Note(TimeSpan lifetime)
    {
        if (lifetime > TimeSpan.Zero)
        {
            Interlocked.Exchange(ref _life, lifetime.Ticks);
        }
    }

    private void Seed(string json)
    {
        var now = _time.GetUtcNow();
        foreach (var laid in Read(json))
        {
            var until = now + laid.Left;
            var entry = new DnsEntry(laid.Rule, laid.Address);
            if (_held.TryAdd(Key(laid.Rule, laid.Address), new Held(entry, until - laid.Life / 2, until, now)))
            {
                Interlocked.Increment(ref _stirs);
            }
        }
    }

    private static List<Laid> Read(string json)
    {
        if (json.Length == 0)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return [.. document.RootElement.GetProperty("nftables").EnumerateArray().SelectMany(Elements)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    private static IEnumerable<Laid> Elements(JsonElement item)
    {
        if (!item.TryGetProperty("set", out var set)
            || !set.TryGetProperty("name", out var name)
            || !set.TryGetProperty("elem", out var elements))
        {
            yield break;
        }

        if (RouteRuleset.NameSetRule(name.GetString() ?? string.Empty) is not { } rule)
        {
            yield break;
        }

        var life = Number(set, "timeout");
        foreach (var element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("elem", out var inner)
                || !inner.TryGetProperty("val", out var value)
                || value.ValueKind != JsonValueKind.String
                || !IPAddress.TryParse(value.GetString(), out var address))
            {
                continue;
            }

            var left = Number(inner, "expires");
            var own = Number(inner, "timeout");
            if (left > 0)
            {
                yield return new Laid(rule, address, TimeSpan.FromSeconds(left), TimeSpan.FromSeconds(own > 0 ? own : life));
            }
        }
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static HashSet<long> Live(RoutePlan? plan) =>
        plan is null
            ? []
            : [.. plan.Legs.Where(leg => leg.IsOnHost && leg.Domains.Count > 0).Select(leg => leg.Rule.Id)];

    private sealed record Held(DnsEntry Entry, DateTimeOffset Again, DateTimeOffset Until, DateTimeOffset Seen);

    private sealed record Laid(long Rule, IPAddress Address, TimeSpan Left, TimeSpan Life);
}
