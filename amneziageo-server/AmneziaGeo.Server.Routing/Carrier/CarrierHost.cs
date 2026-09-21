using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Outbound;

namespace AmneziaGeo.Server.Routing.Carrier;

/// <summary>
/// Holds one websocket carrier per outbound that speaks through a wstunnel proxy.
/// </summary>
public sealed class CarrierHost : IDisposable
{
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);

    private readonly Lock _sync = new();

    private readonly Action<string, Exception?>? _note;

    private bool _disposed;

    /// <summary>
    /// ctor
    /// </summary>
    public CarrierHost(Action<string, Exception?>? note = null)
    {
        _note = note;
    }

    /// <summary>
    /// Returns the loopback port the interface of an outbound speaks to, opening a carrier where the proxy changed.
    /// </summary>
    public async Task<int> RaiseAsync(OutboundConfig outbound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var proxy = WsEndpoint.Parse(outbound.Proxy, outbound.Port, outbound.Host);
        if (proxy.Host.Length == 0)
        {
            throw new HostNetworkException($"'{outbound.Name}' names no websocket proxy");
        }

        var address = await HostAddress.ResolveAsync(proxy.Host, ct).ConfigureAwait(false);
        var mark = $"{proxy.Host}|{proxy.Port}|{proxy.PathPrefix}|{proxy.Credentials}|{address}|{outbound.Port}|{outbound.PublicKey}|{outbound.PeerKey}";
        var stale = default(WsCarrier);
        var carrier = default(WsCarrier);
        lock (_sync)
        {
            if (_held.TryGetValue(outbound.Name, out var live))
            {
                if (live.Mark == mark)
                {
                    return live.Carrier.LocalPort;
                }

                stale = live.Carrier;
            }

            carrier = WsCarrier.Start(proxy, address, outbound.Port, Token(outbound), null, _note);
            _held[outbound.Name] = new Held(carrier, mark);
        }

        stale?.Dispose();
        _note?.Invoke($"'{outbound.Name}' carries its tunnel to {proxy.Host}:{proxy.Port} from port {carrier.LocalPort}", null);

        return carrier.LocalPort;
    }

    /// <summary>
    /// Takes the carrier of an outbound down.
    /// </summary>
    public void Withdraw(string name)
    {
        var carrier = default(WsCarrier);
        lock (_sync)
        {
            if (_held.Remove(name, out var held))
            {
                carrier = held.Carrier;
            }
        }

        carrier?.Dispose();
    }

    /// <summary>
    /// Returns the loopback port an outbound speaks to, zero where it carries nothing.
    /// </summary>
    public int PortOf(string name)
    {
        lock (_sync)
        {
            return _held.TryGetValue(name, out var held) ? held.Carrier.LocalPort : 0;
        }
    }

    /// <summary>
    /// Takes every carrier down.
    /// </summary>
    public void Dispose()
    {
        var carriers = default(List<WsCarrier>);
        lock (_sync)
        {
            _disposed = true;
            carriers = [.. _held.Values.Select(one => one.Carrier)];
            _held.Clear();
        }

        foreach (var carrier in carriers)
        {
            carrier.Dispose();
        }
    }

    // The header an outbound proves its keys with to the front of its server.
    private static Func<string>? Token(OutboundConfig outbound) =>
        Curve25519.IsKey(outbound.PrivateKey) && Curve25519.IsKey(outbound.PeerKey)
            ? () => PeerToken.Header(outbound.PrivateKey, outbound.PeerKey, DateTimeOffset.UtcNow)
            : null;

    private sealed record Held(WsCarrier Carrier, string Mark);
}
