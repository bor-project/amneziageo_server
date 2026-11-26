using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Configs;

public sealed record Ipv6Reach(bool Available, string Reason);

public interface IIpv6Probe
{
    Task<Ipv6Reach> CheckAsync(CancellationToken ct);
}

public sealed class Ipv6Probe : IIpv6Probe
{
    private static readonly IPAddress[] Targets =
    [
        IPAddress.Parse("2606:4700:4700::1111"),
        IPAddress.Parse("2001:4860:4860::8888"),
    ];

    private readonly IHostCommands _commands;
    private readonly IHostNetwork _network;
    private readonly ILogger<Ipv6Probe> _logger;
    private readonly Func<IPAddress, IPAddress, CancellationToken, Task<bool>> _connect;
    private readonly TimeSpan _limit;
    private readonly bool _supported;

    /// <summary>
    /// ctor
    /// </summary>
    public Ipv6Probe(IHostCommands commands, IHostNetwork network, ILogger<Ipv6Probe> logger)
        : this(commands, network, logger, ConnectAsync, TimeSpan.FromSeconds(3), Socket.OSSupportsIPv6)
    {
    }

    /// <summary>
    /// ctor
    /// </summary>
    public Ipv6Probe(
        IHostCommands commands,
        IHostNetwork network,
        ILogger<Ipv6Probe> logger,
        Func<IPAddress, IPAddress, CancellationToken, Task<bool>> connect,
        TimeSpan limit,
        bool supported)
    {
        _commands = commands;
        _network = network;
        _logger = logger;
        _connect = connect;
        _limit = limit;
        _supported = supported;
    }

    /// <summary>
    /// Checks the IPv6 path of VPN clients.
    /// </summary>
    public async Task<Ipv6Reach> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_supported)
        {
            return new(false, "unsupported");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_limit);
        try
        {
            var uplink = await _network.UplinkAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            var checks = Targets.Select(target => CheckAsync(target, uplink, deadline.Token, ct)).ToArray();
            var answers = await Task.WhenAll(checks).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (answers.Any(one => one.Available))
            {
                return new(true, "available");
            }

            var reason = new[] { "unreachable", "uplink-mismatch", "no-source", "probe-failed", "unsupported", "no-route" }
                .First(one => answers.Any(answer => answer.Reason == one));
            return new(false, reason);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "unreachable");
        }
        catch (Exception ex) when (ex is HostNetworkException or SocketException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "IPv6 path could not be checked");
            return new(false, "probe-failed");
        }
    }

    private async Task<Ipv6Reach> CheckAsync(IPAddress target, string uplink, CancellationToken deadline, CancellationToken caller)
    {
        try
        {
            var result = await _commands.RunAsync("ip", ["-j", "-6", "route", "get", target.ToString()], null, deadline)
                .WaitAsync(deadline).ConfigureAwait(false);
            if (!result.IsOk)
            {
                var missing = result.Complaint.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
                    || result.Complaint.Contains("No route", StringComparison.OrdinalIgnoreCase)
                    || result.Complaint.Contains("No such process", StringComparison.OrdinalIgnoreCase);
                return new(false, result.Code == 127 ? "unsupported" : missing ? "no-route" : "probe-failed");
            }

            using var document = JsonDocument.Parse(result.Output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new(false, "probe-failed");
            }
            if (document.RootElement.GetArrayLength() == 0)
            {
                return new(false, "no-route");
            }

            var route = document.RootElement[0];
            if (route.ValueKind != JsonValueKind.Object)
            {
                return new(false, "probe-failed");
            }
            if (Text(route, "type") is { Length: > 0 } type && type != "unicast")
            {
                return new(false, "no-route");
            }
            var device = Text(route, "dev");
            if (device.Length == 0)
            {
                return new(false, "no-route");
            }
            var source = Text(route, "prefsrc");
            source = source.Length > 0 ? source : Text(route, "src");
            if (!IPAddress.TryParse(source, out var address) || !Global(address))
            {
                return new(false, "no-source");
            }
            if (!string.Equals(device, uplink, StringComparison.Ordinal))
            {
                return new(false, "uplink-mismatch");
            }

            var reached = await _connect(address, target, deadline).WaitAsync(deadline).ConfigureAwait(false);
            return new(reached, reached ? "available" : "unreachable");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            return new(false, "unreachable");
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "IPv6 connection to {Target} did not succeed", target);
            return new(false, "unreachable");
        }
        catch (Exception ex) when (ex is HostNetworkException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            _logger.LogDebug(ex, "IPv6 path to {Target} could not be checked", target);
            return new(false, "probe-failed");
        }
    }

    private static bool Global(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String ? found.GetString() ?? "" : "";

    private static async Task<bool> ConnectAsync(IPAddress source, IPAddress target, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(source, 0));
        await socket.ConnectAsync(new IPEndPoint(target, 443), ct).ConfigureAwait(false);
        return true;
    }
}
