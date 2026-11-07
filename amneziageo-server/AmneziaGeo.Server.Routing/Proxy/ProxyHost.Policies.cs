using AmneziaGeo.Server.Core.Proxy;

namespace AmneziaGeo.Server.Routing.Proxy;

public sealed partial class ProxyHost
{
    private readonly Dictionary<long, LeasePlan> _policies = [];

    /// <summary>
    /// Applies the policies of linked legacy fronts.
    /// </summary>
    public void ApplyLegacyPolicies(IReadOnlyDictionary<long, (ProxyTargets? Targets, int Port)> policies)
    {
        var desired = policies.ToDictionary(pair => pair.Key, pair =>
        {
            var policy = ProxyTargetRules.TryNormalize(pair.Value.Targets, out var normalized) ? normalized : ProxyTargetRules.Denied;
            var effective = policy is { Restricted: false } ? null : policy;
            return ProxyTargetRules.Write(effective) + "\0" + pair.Value.Port;
        });
        var retired = new List<LeasePlan>();
        lock (_leaseSync)
        {
            foreach (var (id, previous) in _policies.ToArray())
            {
                if (!desired.TryGetValue(id, out var text) || text != previous.Text)
                {
                    _policies.Remove(id);
                    retired.Add(previous);
                }
            }

            foreach (var (id, text) in desired)
            {
                if (!_policies.ContainsKey(id))
                {
                    _policies[id] = new LeasePlan(text) { Ready = true };
                }
            }
        }

        foreach (var previous in retired)
        {
            Revoke(previous);
        }
    }

    /// <summary>
    /// Takes the current policy of a linked legacy front.
    /// </summary>
    public bool TryLegacyLease(long id, out ProxyLease lease)
    {
        lock (_leaseSync)
        {
            lease = default!;
            return _policies.TryGetValue(id, out var plan) && plan.Lifetime.TryLease(out lease);
        }
    }
}
