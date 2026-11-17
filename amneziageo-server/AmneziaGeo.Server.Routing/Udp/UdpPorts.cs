using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Routing.Host;
using System.Net;

namespace AmneziaGeo.Server.Routing.Udp;

/// <summary>
/// Maps public UDP ports to private interface listeners.
/// </summary>
public sealed class UdpPorts : IDisposable
{
    private readonly HostTurn _changes = new();
    private volatile IReadOnlyDictionary<string, int> _vpn = new Dictionary<string, int>();
    private volatile IReadOnlyDictionary<int, int> _quic = new Dictionary<int, int>();
    private Func<IPEndPoint, IPEndPoint?>? _resolve;

    public IReadOnlyDictionary<string, int> Vpn => _vpn;

    public IReadOnlyDictionary<int, int> Quic => _quic;

    public Task<IDisposable> HoldAsync(CancellationToken ct) => _changes.HoldAsync(ct);

    public IPEndPoint Public(IPEndPoint endpoint) => _resolve?.Invoke(endpoint) ?? endpoint;

    public void SetResolver(Func<IPEndPoint, IPEndPoint?>? resolve) => _resolve = resolve;

    public ServerConfig Runtime(ServerConfig config) => _vpn.TryGetValue(config.Name, out var port)
        ? config with { ListenPort = port, UdpPrivate = true } : config;

    public void Set(IReadOnlyDictionary<string, int> vpn, IReadOnlyDictionary<int, int> quic)
    {
        _vpn = vpn;
        _quic = quic;
    }

    public void Dispose() => _changes.Dispose();
}
