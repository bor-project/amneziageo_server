using AmneziaGeo.Server.Core.Proxy;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Routing.Proxy;

public sealed partial class ProxyHost
{
    private readonly object _leaseSync = new();
    private readonly Dictionary<string, LeasePlan> _leases = new(StringComparer.Ordinal);

    /// <summary>
    /// Reports whether the current proxy configuration serves requests.
    /// </summary>
    public bool CanRelay(string name)
    {
        lock (_leaseSync)
        {
            return _leases.TryGetValue(name, out var plan) && plan.Ready && plan.Lifetime.IsActive;
        }
    }

    /// <summary>
    /// Takes the lifetime of the current proxy configuration.
    /// </summary>
    public bool TryLease(string name, out ProxyLease lease)
    {
        lock (_leaseSync)
        {
            lease = default!;
            return _leases.TryGetValue(name, out var plan) && plan.Ready && plan.Lifetime.TryLease(out lease);
        }
    }

    private LeasePlan Desire(string name, string text, bool replace = false)
    {
        var changed = Swap();
        Revoke(changed.Previous);
        return changed.Current;

        (LeasePlan Current, LeasePlan? Previous) Swap()
        {
            lock (_leaseSync)
            {
                _leases.TryGetValue(name, out var previous);
                if (!replace && previous is not null && previous.Text == text && previous.Lifetime.IsActive)
                {
                    return (previous, null);
                }

                var current = new LeasePlan(text);
                _leases[name] = current;
                return (current, previous);
            }
        }
    }

    private void Confirm(string name, LeasePlan plan)
    {
        lock (_leaseSync)
        {
            if (_leases.GetValueOrDefault(name) == plan && plan.Lifetime.IsActive)
            {
                plan.Ready = true;
            }
        }
    }

    private void Retire(string name)
    {
        var previous = Remove();
        Revoke(previous);

        LeasePlan? Remove()
        {
            lock (_leaseSync)
            {
                return _leases.Remove(name, out var plan) ? plan : null;
            }
        }
    }

    private void Revoke(LeasePlan? plan)
    {
        if (plan is null)
        {
            return;
        }

        try
        {
            plan.Lifetime.Dispose();
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "Canceling proxy streams failed");
        }
    }

    private sealed class LeasePlan(string text)
    {
        public string Text { get; } = text;
        public ProxyLifetime Lifetime { get; } = new();
        public volatile bool Ready;
    }
}
