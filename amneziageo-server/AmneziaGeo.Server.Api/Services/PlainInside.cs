using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Awg.Config;
using Microsoft.AspNetCore.Connections;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// The hello and the measurement inside the tunnel go over plain HTTP: the answer of the hello is sealed by the keys of
/// the config and a leg of the measurement is let in by its pass, so no certificate stands for anything there. The
/// port of the services takes such a request only at an address the endpoint holds inside the tunnel, and only on a
/// port of its own: the port the panel shares stays on TLS.
/// </summary>
public static class PlainInside
{
    /// <summary>
    /// Returns the addresses the endpoint holds inside the tunnel.
    /// </summary>
    public static IReadOnlyList<IPAddress> Addresses(ServerConfig endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return [.. endpoint.Address
            .Select(range => IPAddress.TryParse(range.Split('/')[0].Trim(), out var found) ? found : null)
            .OfType<IPAddress>()];
    }

    /// <summary>
    /// Returns the address the tunnel reaches the endpoint at, the first one of IPv4 ahead of the rest; null where it
    /// holds none.
    /// </summary>
    public static IPAddress? Address(ServerConfig endpoint)
    {
        var addresses = Addresses(endpoint);

        return addresses.FirstOrDefault(one => one.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
    }

    /// <summary>
    /// Returns where the services of the endpoint answer plain inside the tunnel; null where the endpoint holds no
    /// address there or its port is the one the panel shares.
    /// </summary>
    public static string? Origin(ServerConfig endpoint, int port, ServiceShare? share)
    {
        if (Address(endpoint) is not { } address || share?.Point?.Port == port)
        {
            return null;
        }

        return Root("http", address.ToString(), port);
    }

    /// <summary>
    /// Returns the root of an address of the services, with the brackets an IPv6 host takes.
    /// </summary>
    public static string Root(string scheme, string host, int port)
    {
        var shown = IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;

        return string.Create(CultureInfo.InvariantCulture, $"{scheme}://{shown}:{port}");
    }

    /// <summary>
    /// Tells whether a plain request asks for the hello or a leg of the measurement, the only ones it reaches.
    /// </summary>
    public static bool Serves(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Path.Equals(ServiceDesk.HelloPath, StringComparison.Ordinal)
            || request.Path.StartsWithSegments(ServiceDesk.SpeedPath, StringComparison.Ordinal);
    }
}

/// <summary>
/// Sends a connection to the port of the services through TLS, or past it when the connection opens plain at an
/// address an endpoint of the port holds inside the tunnel.
/// </summary>
internal sealed class PlainGate
{
    // The first byte of a TLS record that opens a handshake.
    private const byte Handshake = 0x16;

    private static readonly TimeSpan FirstByteWait = TimeSpan.FromSeconds(10);

    private readonly Func<ServicePoint> _point;

    private ConnectionDelegate? _plain;

    /// <summary>
    /// ctor
    /// </summary>
    public PlainGate(Func<ServicePoint> point)
    {
        _point = point;
    }

    /// <summary>
    /// Holds the step after TLS, where a plain connection goes on.
    /// </summary>
    public ConnectionDelegate Keep(ConnectionDelegate next)
    {
        _plain = next;

        return next;
    }

    /// <summary>
    /// Sends the connection on: through TLS, or past it when it opens plain inside the tunnel.
    /// </summary>
    public async Task SortAsync(ConnectionContext connection, ConnectionDelegate tls)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tls);

        if (_plain is { } plain && Inside(connection.LocalEndPoint) && await OpensPlainAsync(connection).ConfigureAwait(false))
        {
            await plain(connection).ConfigureAwait(false);

            return;
        }

        await tls(connection).ConfigureAwait(false);
    }

    // Tells whether the connection came to an address an endpoint of the port holds inside the tunnel.
    private bool Inside(EndPoint? local)
    {
        if (local is not IPEndPoint { Address: var address })
        {
            return false;
        }

        var reached = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return _point().Endpoints.Any(endpoint => endpoint.Inside.Contains(reached));
    }

    // Reads the first byte without taking it: TLS opens with a handshake record, plain HTTP with a word.
    private static async Task<bool> OpensPlainAsync(ConnectionContext connection)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
        wait.CancelAfter(FirstByteWait);
        var input = connection.Transport.Input;
        try
        {
            var read = await input.ReadAsync(wait.Token).ConfigureAwait(false);
            var plain = !read.Buffer.IsEmpty && read.Buffer.FirstSpan[0] != Handshake;
            input.AdvanceTo(read.Buffer.Start);

            return plain;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
