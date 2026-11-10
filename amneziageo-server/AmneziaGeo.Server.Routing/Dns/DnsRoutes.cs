using System.Net;
using AmneziaGeo.Server.Routing.Route;

namespace AmneziaGeo.Server.Routing.Dns;

/// <summary>
/// Keeps upstream connections by DNS policy.
/// </summary>
public sealed class DnsRoutes : IDisposable
{
    public const int MaxPools = 128;

    private readonly Func<RoutePlan?> _plan;

    private DnsSettings _settings;

    private readonly Func<IReadOnlyList<string>, uint, IDnsUpstream> _create;

    private readonly Dictionary<string, Pool> _pools = new(StringComparer.Ordinal);

    private readonly Lock _sync = new();

    private long _turn;

    private bool _closed;

    private string? _fault;

    /// <summary>
    /// ctor
    /// </summary>
    public DnsRoutes(
        Func<RoutePlan?> plan,
        DnsSettings settings,
        Func<IReadOnlyList<string>, uint, IDnsUpstream>? create = null)
    {
        _plan = plan;
        _settings = settings;
        _create = create ?? ((servers, mark) => new DnsUpstream(servers, DnsDefaults.Wait, () => mark));
    }

    public string? Fault => Volatile.Read(ref _fault);

    public void Follow(string old, string anew)
    {
        lock (_sync)
        {
            var settings = _settings;
            if (string.Equals(settings.Outbound, old, StringComparison.Ordinal))
            {
                Volatile.Write(ref _settings, settings with { Outbound = anew });
            }
        }
    }

    public DnsResolution Select(string name, IPAddress? client = null, string? inbound = null)
    {
        var plan = _plan();
        if (string.IsNullOrEmpty(inbound))
        {
            var origins = client is not null && plan is not null
                ? plan.DnsOrigins.Where(origin => origin.Network.Contains(client)).Select(origin => origin.Inbound).Distinct(StringComparer.Ordinal).Take(2).ToArray()
                : [];
            inbound = origins.Length == 1 ? origins[0] : null;
        }

        var selected = DnsRouting.Select(Volatile.Read(ref _settings), plan, name, client, inbound, Interlocked.Increment(ref _turn));
        if (selected.Way.Fault is { } fault)
        {
            Volatile.Write(ref _fault, fault.Message);
        }

        return selected;
    }

    public IDnsUpstream ForClient(string name, IPAddress? client = null, string? inbound = null) =>
        new Bound(this, Select(name, client, inbound));

    public IDnsUpstream ForRule(DnsWarmName name) => new Bound(this,
        DnsRouting.Select(Volatile.Read(ref _settings), _plan(), name.Name, turn: Interlocked.Increment(ref _turn), rule: name.Rule));

    public async Task<byte[]?> AskAsync(DnsResolution selected, ReadOnlyMemory<byte> question, bool stream, CancellationToken ct)
    {
        if (selected.Way.Fault is { } fault)
        {
            Volatile.Write(ref _fault, fault.Message);

            return null;
        }

        var pool = Take(selected);
        if (pool is null)
        {
            return null;
        }

        try
        {
            var answer = await pool.Upstream.AskAsync(question, stream, ct).ConfigureAwait(false);
            Volatile.Write(ref _fault, answer is null ? $"no name server answers through '{selected.Way.Outbound}'" : null);

            return answer;
        }
        finally
        {
            Release(selected.Key, pool);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _closed = true;
            foreach (var pair in _pools.Where(pair => pair.Value.Users == 0).ToArray())
            {
                _pools.Remove(pair.Key);
                (pair.Value.Upstream as IDisposable)?.Dispose();
            }
        }
    }

    private Pool? Take(DnsResolution selected)
    {
        lock (_sync)
        {
            if (_closed || selected.Way.Mark is null || selected.Servers.Count == 0)
            {
                return null;
            }

            if (_pools.TryGetValue(selected.Key, out var held))
            {
                held.Users++;
                held.Seen = Environment.TickCount64;

                return held;
            }

            if (_pools.Count >= MaxPools)
            {
                var spare = _pools.Where(pair => pair.Value.Users == 0).OrderBy(pair => pair.Value.Seen).FirstOrDefault();
                if (spare.Value is null)
                {
                    Volatile.Write(ref _fault, "all DNS connection pools are busy");

                    return null;
                }

                _pools.Remove(spare.Key);
                (spare.Value.Upstream as IDisposable)?.Dispose();
            }

            var added = new Pool(_create(selected.Servers, selected.Way.Mark.Value));
            _pools.Add(selected.Key, added);

            return added;
        }
    }

    private void Release(string key, Pool pool)
    {
        lock (_sync)
        {
            pool.Users--;
            if (_closed && pool.Users == 0)
            {
                _pools.Remove(key);
                (pool.Upstream as IDisposable)?.Dispose();
            }
        }
    }

    private sealed class Pool(IDnsUpstream upstream)
    {
        public IDnsUpstream Upstream { get; } = upstream;

        public int Users { get; set; } = 1;

        public long Seen { get; set; } = Environment.TickCount64;
    }

    private sealed class Bound(DnsRoutes routes, DnsResolution selected) : IDnsUpstream
    {
        public Task<byte[]?> AskAsync(ReadOnlyMemory<byte> question, bool stream, CancellationToken ct) =>
            routes.AskAsync(selected, question, stream, ct);
    }
}
