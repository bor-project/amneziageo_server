using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// One endpoint as the reading of names needs it.
/// </summary>
/// <param name="Name">The interface of the endpoint.</param>
/// <param name="Networks">The networks its clients live in.</param>
/// <param name="Closed">The ranges it keeps its clients out of.</param>
public sealed record NameEndpoint(string Name, IReadOnlyList<IPNetwork> Networks, IReadOnlyList<IPNetwork> Closed);

/// <summary>
/// Tells which endpoint a client arrives on and which addresses the panel may carry its connections to.
/// </summary>
public sealed class NameReach
{
    /// <summary>
    /// The reach of a panel that holds no endpoint.
    /// </summary>
    public static readonly NameReach None = new([]);

    private static readonly IPNetwork[] Fixed =
        [.. NameDefaults.Closed4.Concat(NameDefaults.Closed6).Select(range => IPNetwork.Parse(range))];

    private readonly IReadOnlyList<NameEndpoint> _endpoints;

    /// <summary>
    /// ctor
    /// </summary>
    public NameReach(IReadOnlyList<NameEndpoint> endpoints) => _endpoints = endpoints;

    /// <summary>
    /// Returns the reach the endpoints of the panel give.
    /// </summary>
    public static NameReach Of(IReadOnlyList<ServerConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        return new NameReach([.. configs.Select(config => new NameEndpoint(config.Name, Ranges(config.Address), Ranges(config.Blocked)))]);
    }

    /// <summary>
    /// Tells whether an address lies in a range the panel never carries a connection to: its own, private and group
    /// ones.
    /// </summary>
    public static bool IsClosed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var plain = Plain(address);

        return Array.Exists(Fixed, range => range.Contains(plain));
    }

    /// <summary>
    /// Returns the interface of the endpoint a client arrives on, empty when it is a client of none.
    /// </summary>
    public string Inbound(IPAddress client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var plain = Plain(client);

        return _endpoints.FirstOrDefault(one => Holds(one.Networks, plain))?.Name ?? string.Empty;
    }

    /// <summary>
    /// Tells whether the panel may carry a connection of a client to an address itself: the address is none the host
    /// keeps to itself, no client of an endpoint and none the endpoint of the client keeps its clients out of.
    /// </summary>
    public bool Lets(IPAddress client, IPAddress target)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(target);

        var plain = Plain(target);
        if (IsClosed(plain) || _endpoints.Any(one => Holds(one.Networks, plain)))
        {
            return false;
        }

        var from = Plain(client);
        var own = _endpoints.Where(one => Holds(one.Networks, from)).ToArray();

        return !(own.Length > 0 ? own : _endpoints).Any(one => Holds(one.Closed, plain));
    }

    private static bool Holds(IReadOnlyList<IPNetwork> ranges, IPAddress address) =>
        ranges.Any(range => range.BaseAddress.AddressFamily == address.AddressFamily && range.Contains(address));

    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IReadOnlyList<IPNetwork> Ranges(IReadOnlyList<string> ranges)
    {
        var held = new List<IPNetwork>();
        foreach (var range in ranges)
        {
            if (AwgAllowedIp.TryParse(range, out var parsed))
            {
                held.Add(new IPNetwork(parsed.Network().Address, parsed.Cidr));
            }
        }

        return held;
    }
}
