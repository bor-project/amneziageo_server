using System.Collections.Concurrent;
using System.Net;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Routing.Access;

/// <summary>
/// Remembers which name the resolver answered with an address for, to put the name on the records.
/// </summary>
public sealed class AccessNames
{
    private readonly int _limit;

    private readonly Lock _turn = new();

    private ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string> _fresh = new();

    private ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string> _old = new();

    private volatile bool _isOn;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessNames(int limit = AccessDefaults.Names) => _limit = limit;

    /// <summary>
    /// Whether the answers are remembered.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            _isOn = value;
            if (!value)
            {
                Forget();
            }
        }
    }

    /// <summary>
    /// The names held now.
    /// </summary>
    public int Count => _fresh.Count + _old.Count;

    /// <summary>
    /// Remembers the addresses of an answer a client was given.
    /// </summary>
    public void Hear(IPAddress? client, DnsMessage answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (!_isOn || answer.Question.Length == 0)
        {
            return;
        }

        var name = answer.Question.TrimEnd('.').ToLowerInvariant();
        var fresh = _fresh;
        foreach (var address in answer.Addresses)
        {
            var target = Plain(address);
            fresh[(null, target)] = name;
            if (client is not null)
            {
                fresh[(Plain(client), target)] = name;
            }
        }

        if (fresh.Count > _limit)
        {
            Turn(fresh);
        }
    }

    /// <summary>
    /// Returns the name a client was last given an address for, or empty when none is known.
    /// </summary>
    public string Name(IPAddress client, IPAddress target)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(target);

        var own = (Plain(client), Plain(target));
        var any = ((IPAddress?)null, Plain(target));
        var fresh = _fresh;
        var old = _old;

        return fresh.TryGetValue(own, out var name)
            || old.TryGetValue(own, out name)
            || fresh.TryGetValue(any, out name)
            || old.TryGetValue(any, out name)
            ? name
            : string.Empty;
    }

    private void Turn(ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string> full)
    {
        lock (_turn)
        {
            if (!ReferenceEquals(full, _fresh))
            {
                return;
            }

            _old = full;
            _fresh = new ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string>();
        }
    }

    private void Forget()
    {
        lock (_turn)
        {
            _fresh = new ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string>();
            _old = new ConcurrentDictionary<(IPAddress? Client, IPAddress Target), string>();
        }
    }

    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
