using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Client;

public sealed record ClientAddressPick(IReadOnlyList<string> Addresses, ClientFault? Fault);
public sealed record ClientIpv6Plan(IReadOnlyList<TunnelClient> Clients, ClientFault? Fault);

public static class ClientIpv6
{
    /// <summary>
    /// Completes a client's IPv6 addresses.
    /// </summary>
    public static ClientAddressPick Complete(IReadOnlyList<string> ranges, IReadOnlyList<string> addresses, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(taken);

        var own = Ranges(ranges);
        if (!own.Any(one => one.IsSix))
        {
            return new(addresses, null);
        }
        var held = Points(taken).Concat(own.Select(one => one.Address)).ToHashSet();
        return Complete(own, addresses, held);
    }

    /// <summary>
    /// Plans IPv6 addresses for the clients of an interface.
    /// </summary>
    public static ClientIpv6Plan Plan(ServerConfig config, IReadOnlyList<TunnelClient> clients)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(clients);

        var own = Ranges(config.Address);
        if (!own.Any(one => one.IsSix))
        {
            return new(clients, null);
        }
        var members = clients.Select((client, index) => (Client: client, Index: index))
            .Where(one => one.Client.ConfigId == config.Id).OrderBy(one => one.Client.Id).ThenBy(one => one.Index).ToArray();
        var counts = new Dictionary<IPAddress, int>();
        var held = new HashSet<IPAddress>();
        foreach (var point in own.Select(one => one.Address).Concat(members.SelectMany(one => Points(one.Client.Address))))
        {
            Take(point);
        }
        var projected = clients.ToArray();
        var changed = false;
        foreach (var member in members)
        {
            foreach (var point in Points(member.Client.Address))
            {
                var count = counts[point] - 1;
                if (count == 0)
                {
                    counts.Remove(point);
                    held.Remove(point);
                }
                else
                {
                    counts[point] = count;
                }
            }
            var picked = Complete(own, member.Client.Address, held);
            if (picked.Fault is not null)
            {
                return new(clients, picked.Fault);
            }
            if (!ReferenceEquals(picked.Addresses, member.Client.Address))
            {
                projected[member.Index] = member.Client with { Address = picked.Addresses };
                changed = true;
            }
            foreach (var point in Points(picked.Addresses))
            {
                Take(point);
            }
        }
        return new(changed ? projected : clients, null);

        void Take(IPAddress point)
        {
            counts[point] = counts.GetValueOrDefault(point) + 1;
            held.Add(point);
        }
    }

    private static ClientAddressPick Complete(IReadOnlyList<AwgAllowedIp> own, IReadOnlyList<string> addresses, HashSet<IPAddress> occupied)
    {
        if (addresses.Count > ClientRules.MaxAddresses)
        {
            return Limit(addresses);
        }
        var six = own.Where(one => one.IsSix).ToArray();
        var current = Ranges(addresses);
        var endpoints = own.Select(one => one.Address).ToHashSet();
        var seen = new HashSet<IPAddress>();
        foreach (var address in current.Where(one => one.IsSix))
        {
            var covering = six.Where(one => ClientPool.Inside(one, address.Address)).ToArray();
            if (covering.Length == 0)
            {
                continue;
            }
            if (address.Cidr != 128)
            {
                return new(addresses, new ClientFault("bad-client-address", "the client IPv6 address is not a single address"));
            }
            if (endpoints.Contains(address.Address) || covering.Any(one => ClientPool.Reserved(one, ClientPool.Number(one, address.Address))))
            {
                return new(addresses, new ClientFault("client-address-reserved", "the client IPv6 address is reserved by its interface"));
            }
            if (!seen.Add(address.Address) || occupied.Contains(address.Address))
            {
                return new(addresses, new ClientFault("client-address-taken", "another client carries the IPv6 address"));
            }
        }
        var preferred = Preferred(own, current);
        var held = occupied;
        held.UnionWith(current.Select(one => one.Address));
        held.UnionWith(endpoints);
        var result = new List<string>(addresses);
        foreach (var range in six)
        {
            if (current.Any(one => one.IsSix && ClientPool.Inside(range, one.Address)))
            {
                continue;
            }
            if (result.Count == ClientRules.MaxAddresses)
            {
                return Limit(addresses);
            }
            var point = ClientPool.Pick(range, held, preferred);
            if (point is null)
            {
                return new(addresses, new ClientFault("client-ipv6-unavailable", "the interface carries no free IPv6 address"));
            }
            result.Add($"{point}/128");
            current.Add(new AwgAllowedIp(point, 128));
            held.Add(point);
        }
        return new(result.Count == addresses.Count ? addresses : result.ToArray(), null);
    }

    private static UInt128? Preferred(IReadOnlyList<AwgAllowedIp> ranges, IReadOnlyList<AwgAllowedIp> addresses)
    {
        foreach (var address in addresses.Where(one => !one.IsSix && one.Cidr == 32))
        {
            foreach (var range in ranges.Where(one => !one.IsSix))
            {
                if (ClientPool.Inside(range, address.Address))
                {
                    return ClientPool.Number(range, address.Address);
                }
            }
        }
        return null;
    }

    private static ClientAddressPick Limit(IReadOnlyList<string> addresses) =>
        new(addresses, new ClientFault("client-ipv6-address-limit", $"the client would carry more than {ClientRules.MaxAddresses} addresses"));

    private static List<AwgAllowedIp> Ranges(IEnumerable<string> texts) =>
        [.. texts.Select(text => AwgAllowedIp.TryParse(text, out var range) ? range : null).OfType<AwgAllowedIp>()
            .Select(range => range.IsSix && range.Address.ScopeId != 0
                ? range with { Address = new IPAddress(range.Address.GetAddressBytes()) } : range)];

    private static IEnumerable<IPAddress> Points(IEnumerable<string> texts) => Ranges(texts).Select(one => one.Address);
}
