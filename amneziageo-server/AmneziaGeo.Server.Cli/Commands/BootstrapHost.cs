using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using AmneziaGeo.Server.Routing.Dns;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Supplies installation network checks.
/// </summary>
public interface IBootstrapHost
{
    IReadOnlyList<IPAddress> Addresses { get; }

    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);

    IDisposable Reserve(IPAddress address, int port, ProtocolType protocol);

    string? VpnFault();

    Task<bool> DnsAsync(string upstream, CancellationToken ct);

    Task<bool> UpdatesAsync(Uri address, string token, CancellationToken ct);
}

/// <summary>
/// Checks the network and VPN prerequisites of the container.
/// </summary>
public sealed class BootstrapHost : IBootstrapHost
{
    public IReadOnlyList<IPAddress> Addresses => NetworkInterface.GetAllNetworkInterfaces()
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(n => n.Address).Distinct().ToArray();

    /// <inheritdoc/>
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => System.Net.Dns.GetHostAddressesAsync(host, ct);

    /// <inheritdoc/>
    public IDisposable Reserve(IPAddress address, int port, ProtocolType protocol)
    {
        var socket = new Socket(address.AddressFamily, protocol == ProtocolType.Tcp ? SocketType.Stream : SocketType.Dgram, protocol);
        try
        {
            socket.ExclusiveAddressUse = true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = false;
            }

            socket.Bind(new IPEndPoint(address, port));

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public string? VpnFault()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/sys/module/amneziawg"))
        {
            return "the host must load the amneziawg kernel module";
        }

        var caps = File.ReadLines("/proc/self/status").FirstOrDefault(line => line.StartsWith("CapEff:", StringComparison.Ordinal))?.Split(':')[1].Trim();
        if (!ulong.TryParse(caps, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var effective) || (effective & (1UL << 12)) == 0)
        {
            return "NET_ADMIN capability is required to configure VPN interfaces";
        }

        if (File.ReadAllText("/proc/sys/net/ipv4/ip_forward").Trim() != "1")
        {
            return "IPv4 forwarding is disabled on the host";
        }

        return File.ReadAllText("/proc/sys/net/ipv6/conf/all/forwarding").Trim() != "1"
            ? "IPv6 forwarding is disabled on the host" : null;
    }

    /// <inheritdoc/>
    public async Task<bool> DnsAsync(string upstream, CancellationToken ct)
    {
        using var link = new DnsUpstream([upstream], TimeSpan.FromSeconds(5));
        var question = DnsQuestion.Packet(".", DnsRecordType.A, DnsQuestion.Id());
        var answer = await link.AskAsync(question, false, ct).ConfigureAwait(false);

        return answer is not null && DnsMessage.Read(answer) is { IsResponse: true, Code: 0 };
    }

    /// <inheritdoc/>
    public async Task<bool> UpdatesAsync(Uri address, string token, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 65536 };
        using var request = new HttpRequestMessage(HttpMethod.Get, address.AbsoluteUri.TrimEnd('/') + "/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));

        return json.RootElement.TryGetProperty("writable", out var writable) && writable.ValueKind == JsonValueKind.True
            && json.RootElement.TryGetProperty("releases", out var count) && count.TryGetInt32(out _);
    }
}
