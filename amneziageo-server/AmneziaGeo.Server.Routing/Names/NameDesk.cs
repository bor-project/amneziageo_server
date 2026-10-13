using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// What the rules do with a connection whose name was read.
/// </summary>
/// <param name="Verdict">What happens to the connection, as the route tester names it.</param>
/// <param name="Leg">The rule that decided, or null when none did.</param>
/// <param name="Mark">The mark the connection leaves with, 0 for the way out of the host.</param>
public sealed record NameDecision(string Verdict, RouteLeg? Leg, uint Mark)
{
    /// <summary>
    /// Tells whether the connection goes on.
    /// </summary>
    public bool Goes => Verdict is RouteProbe.Out or RouteProbe.Host;

    /// <summary>
    /// Tells whether a connection that left with a mark is on the way the rules name already.
    /// </summary>
    public bool Keeps(uint mark) =>
        Goes && (Verdict == RouteProbe.Out && Leg is { } leg ? leg.Exit.Marks.Contains(mark) : mark == 0);

    /// <summary>
    /// Tells whether a connection that left with a mark took another way than the rules name and goes on along it,
    /// the rules taking the next ones by the address.
    /// </summary>
    public bool Misses(uint mark) => Goes && !Keeps(mark);
}

/// <summary>
/// Turns a name read out of a connection into what the rules do with it and into the addresses of their sets.
/// </summary>
public sealed class NameDesk
{
    private readonly ConcurrentQueue<IPAddress> _seen = new();

    private readonly Lock _turn = new();

    private readonly NameMarks _marks = new();

    private readonly DnsSets _sets;

    private readonly IHostNetwork _network;

    private RoutePlan? _plan;

    private DnsNames _names = DnsNames.Build(null);

    private long _learned;

    /// <summary>
    /// ctor
    /// </summary>
    public NameDesk(DnsSets sets, IHostNetwork network)
    {
        _sets = sets;
        _network = network;
    }

    /// <summary>
    /// How many addresses the names read out of the connections brought into the sets of the rules.
    /// </summary>
    public long Learned => Interlocked.Read(ref _learned);

    /// <summary>
    /// Returns the rules of a plan a name falls under.
    /// </summary>
    public IReadOnlyList<long> Match(RoutePlan? plan, string name)
    {
        if (plan is null || name.Length == 0)
        {
            return [];
        }

        lock (_turn)
        {
            if (!ReferenceEquals(plan, _plan))
            {
                _names = DnsNames.Build(plan);
                _plan = plan;
            }

            return _names.Match(name);
        }
    }

    /// <summary>
    /// Puts an address into the sets of the rules its name falls under, telling whether the host has to be told.
    /// </summary>
    public bool Learn(RoutePlan plan, string name, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(address);

        var told = false;
        foreach (var rule in Match(plan, name))
        {
            if (_sets.Add(rule, address, plan.Dns.NameLifetime))
            {
                Interlocked.Increment(ref _learned);
                told = true;
            }
        }

        return told;
    }

    /// <summary>
    /// Sends the addresses that wait into the sets of the rules on the host.
    /// </summary>
    public Task<int> LandAsync(RoutePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return _sets.FlushAsync(plan, plan.Dns.NameLifetime, ct);
    }

    /// <summary>
    /// Returns what the rules do with a connection, the name it goes to counted in.
    /// </summary>
    public NameDecision Decide(RoutePlan plan, RouteQuery query)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(query);

        var verdict = RouteProbe.Test(plan, query, _sets.Holds, Match(plan, query.Name));
        var client = query.Sources.Count > 0 ? query.Sources[0] : IPAddress.Any;
        var mark = verdict.Verdict == RouteProbe.Out && verdict.Leg is { } leg ? _marks.Pick(leg.Exit, client) : 0;

        return new NameDecision(verdict.Verdict, verdict.Leg, mark);
    }

    /// <summary>
    /// Notes an address as looked at, so that the firewall leaves it to the rules for a while.
    /// </summary>
    public void Seen(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _seen.Enqueue(address);
    }

    /// <summary>
    /// Sends the addresses looked at to the host, returning how many went.
    /// </summary>
    public async Task<int> NoteAsync(CancellationToken ct)
    {
        var four = new HashSet<string>(StringComparer.Ordinal);
        var six = new HashSet<string>(StringComparer.Ordinal);
        while (four.Count + six.Count < DnsSets.MaxBatch && _seen.TryDequeue(out var address))
        {
            (address.AddressFamily == AddressFamily.InterNetworkV6 ? six : four).Add(address.ToString());
        }

        if (four.Count + six.Count == 0)
        {
            return 0;
        }

        var text = new StringBuilder();
        Write(text, false, four);
        Write(text, true, six);
        await _network.FirewallAsync(text.ToString(), ct).ConfigureAwait(false);

        return four.Count + six.Count;
    }

    /// <summary>
    /// Sends one address looked at to the host at once.
    /// </summary>
    public Task NoteAsync(IPAddress address, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);

        var text = new StringBuilder();
        Write(text, address.AddressFamily == AddressFamily.InterNetworkV6, [address.ToString()]);

        return _network.FirewallAsync(text.ToString(), ct);
    }

    private static void Write(StringBuilder text, bool six, HashSet<string> addresses)
    {
        if (addresses.Count == 0)
        {
            return;
        }

        text.Append("add element inet ").Append(RouteRuleset.TableName).Append(' ').Append(RouteRuleset.SeenSet(six));
        text.Append(" { ").Append(string.Join(", ", addresses)).Append(" }\n");
    }
}
