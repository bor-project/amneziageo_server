using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Declares expected public addresses and private trust anchors.
/// </summary>
public sealed record BootstrapValidation
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedAddresses { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string> TrustedCertificates { get; init; } = [];
}

/// <summary>
/// Validates runtime conditions before the initial transaction commits.
/// </summary>
public sealed class BootstrapChecks : IDisposable
{
    private readonly IBootstrapHost _host;

    private readonly List<IDisposable> _leases = [];

    private readonly List<(string Field, IPAddress Address, int Port, ProtocolType Protocol, bool Http)> _listeners = [];

    private readonly HashSet<string> _checkedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ctor
    /// </summary>
    public BootstrapChecks(IBootstrapHost? host = null)
    {
        _host = host ?? new BootstrapHost();
    }

    /// <summary>
    /// Checks listeners, names, certificates, storage and upstreams.
    /// </summary>
    public async Task CheckAsync(BootstrapSettings setup, PanelSettings panel, string database, CancellationToken ct)
    {
        ValidateOverrides(setup.Validation);
        Persistent(database, "storage.data");
        Persistent(Environment.GetEnvironmentVariable("AMNEZIAGEO_SIGNING_KEY") ?? "/etc/amneziageo-server/signing.pem", "storage.signingKey");
        WritableDirectory(Path.GetDirectoryName(database)!, "storage.data");
        if (Environment.GetEnvironmentVariable("AMNEZIAGEO_CONTAINER") == "1"
            && (!bool.TryParse(Environment.GetEnvironmentVariable("Proxy__Enabled"), out var proxyEnabled) || proxyEnabled))
        {
            var directory = Environment.GetEnvironmentVariable("Proxy__Directory") ?? "/etc/amneziageo-server";
            Persistent(directory, "runtime.Proxy__Directory");
            WritableDirectory(directory, "runtime.Proxy__Directory");
            foreach (var executable in new[] { "wstunnel", "wstunnel-get", "wstunnel-quic", "AmneziaGeo.Server.Proxy.Host" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, executable);
                if (!File.Exists(path) || (OperatingSystem.IsLinux() && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0))
                {
                    throw new BootstrapFault("runtime.Proxy__Enabled", "the image does not contain the required proxy runtime " + executable);
                }
            }
        }
        foreach (var name in panel.Domains)
        {
            await HostAsync(name, panel.Listen, setup.Validation, "panel.domains", ct).ConfigureAwait(false);
        }

        var chain = panel.Certificate.Length > 0 ? panel.Certificate : Environment.GetEnvironmentVariable("Web__Certificate") ?? string.Empty;
        var key = panel.CertificateKey.Length > 0 ? panel.CertificateKey : Environment.GetEnvironmentVariable("Web__CertificateKey") ?? string.Empty;
        if (Environment.GetEnvironmentVariable("AMNEZIAGEO_HEALTH") is { } health)
        {
            if (!Uri.TryCreate(health, UriKind.Absolute, out var url) || url.Scheme != (chain.Length > 0 ? "https" : "http")
                || url.Port != panel.Port || !(url.IsLoopback || IPAddress.TryParse(url.Host, out var ip) && _host.Addresses.Contains(ip))
                || !url.AbsolutePath.EndsWith("/api/health", StringComparison.Ordinal) || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                throw new BootstrapFault("runtime.AMNEZIAGEO_HEALTH", "health URL must check this panel with its actual scheme/port and /api/health path");
            }
        }

        BootstrapCertificates.Check(chain, key, panel.Domains, setup.Validation.TrustedCertificates, "panel.certificate");
        Add("panel.port", panel.Listen, panel.Port, ProtocolType.Tcp, true);
        var routes = new List<(string Field, string Path, int Port)> { ("panel.path", panel.Path, panel.Port) };

        if (setup.Subscriptions is { } subscriptions)
        {
            if (subscriptions.IsEnabled && subscriptions.Certificate.Length == 0 && chain.Length == 0)
            {
                throw new BootstrapFault("subscriptions.certificate", "an enabled subscriptions service requires its own or the panel TLS certificate");
            }

            if (subscriptions.IsEnabled && !subscriptions.Separate && !setup.Endpoints.Any(e => e.Settings.IsEnabled))
            {
                throw new BootstrapFault("subscriptions.separate", "subscriptions require an enabled VPN interface or a separate listener");
            }

            foreach (var name in subscriptions.Domains)
            {
                await HostAsync(name, subscriptions.Separate ? subscriptions.Listen : panel.Listen, setup.Validation, "subscriptions.domains", ct).ConfigureAwait(false);
            }

            if (subscriptions.Certificate.Length > 0 || subscriptions.CertificateKey.Length > 0)
            {
                BootstrapCertificates.Check(subscriptions.Certificate, subscriptions.CertificateKey, subscriptions.Domains, setup.Validation.TrustedCertificates, "subscriptions.certificate");
            }

            if (subscriptions.IsEnabled && subscriptions.Separate)
            {
                if (subscriptions.Certificate.Length == 0 && chain.Length == 0)
                {
                    throw new BootstrapFault("subscriptions.certificate", "an enabled subscriptions service requires its own or the panel TLS certificate");
                }

                BootstrapCertificates.Check(subscriptions.Certificate.Length > 0 ? subscriptions.Certificate : chain,
                    subscriptions.CertificateKey.Length > 0 ? subscriptions.CertificateKey : key, subscriptions.Domains, setup.Validation.TrustedCertificates, "subscriptions.certificate");
                Add("subscriptions.port", subscriptions.Listen, subscriptions.Port, ProtocolType.Tcp, true);
                routes.Add(("subscriptions.path", subscriptions.Path, subscriptions.Port));
            }
        }

        var enabled = setup.Endpoints.Where(e => e.Settings.IsEnabled).ToArray();
        if (enabled.Length > 0 && _host.VpnFault() is { } fault)
        {
            throw new BootstrapFault("endpoints", fault);
        }

        if (setup.Endpoints.Count > 0)
        {
            var directory = Environment.GetEnvironmentVariable("Endpoints__Directory") ?? "/etc/amnezia/amneziawg";
            Persistent(directory, "storage.endpoints", true);
            WritableDirectory(directory, "storage.endpoints");
        }

        for (var i = 0; i < setup.Endpoints.Count; i++)
        {
            var settings = setup.Endpoints[i].Settings;
            if (settings.Host.Length > 0)
            {
                await HostAsync(settings.Host, [], setup.Validation, "endpoints[" + i + "].settings.host", ct).ConfigureAwait(false);
            }

            if (!settings.IsEnabled)
            {
                continue;
            }

            Add("endpoints[" + i + "].settings.listenPort", [], settings.ListenPort, ProtocolType.Udp, false);
            var port = settings.ServicesPort > 0 ? settings.ServicesPort : settings.ListenPort;
            Add("endpoints[" + i + "].settings.servicesPort", [], port, ProtocolType.Tcp, true);
            if (setup.Subscriptions is { IsEnabled: true, Separate: false } shared)
            {
                if (!routes.Any(r => r.Field == "subscriptions.path" && r.Port == port))
                {
                    routes.Add(("subscriptions.path", shared.Path, port));
                }
            }

            if (settings.WebSocket)
            {
                if (chain.Length == 0)
                {
                    throw new BootstrapFault("endpoints[" + i + "].settings.webSocket", "WebSocket requires the panel TLS certificate");
                }

                if (settings.WebSocketPath.Length == 0)
                {
                    throw new BootstrapFault("endpoints[" + i + "].settings.webSocketPath", "specify a nonempty WebSocket path");
                }

                routes.Add(("endpoints[" + i + "].settings.webSocketPath", settings.WebSocketPath, port));
            }
        }

        if (setup.Dns is { IsEnabled: true } dns)
        {
            var addresses = dns.Listen.Count > 0 ? dns.Listen
                : enabled.SelectMany(e => e.Settings.Address).Select(a => a.Split('/')[0]).ToArray();
            if (addresses.Count == 0)
            {
                throw new BootstrapFault("dns.listen", "specify resolver addresses or an enabled VPN interface");
            }

            Add("dns.port", addresses, dns.Port, ProtocolType.Tcp, false);
            Add("dns.port", addresses, dns.Port, ProtocolType.Udp, false);
            var servers = _host.NameServers(setup.Endpoints.SelectMany(endpoint => endpoint.Settings.Address).Concat(dns.Listen), dns.Port);
            if (servers.Count == 0)
            {
                throw new BootstrapFault("dns.systemServers", "no system DNS server remains after excluding resolver addresses");
            }
            for (var i = 0; i < servers.Count; i++)
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromSeconds(6));
                if (!await ProbeDnsAsync(servers[i], i, limit.Token).ConfigureAwait(false))
                {
                    throw new BootstrapFault("dns.systemServers[" + i + "]", "system DNS server did not return a valid successful reply");
                }
            }
        }

        if (setup.ClientUpdates is { IsEnabled: true } updates)
        {
            if (updates.Own)
            {
                if (chain.Length == 0)
                {
                    throw new BootstrapFault("clientUpdates", "an enabled client updates service requires the panel TLS certificate");
                }

                var directory = updates.Directory.Length > 0 ? updates.Directory : Path.Combine(Path.GetDirectoryName(database)!, "client-updates");
                Persistent(directory, "clientUpdates.directory");
                WritableDirectory(directory, "clientUpdates.directory");
                Add("clientUpdates.port", [], updates.PortOr(panel.Port), ProtocolType.Tcp, true);
                routes.Add(("clientUpdates.path", updates.Path, updates.PortOr(panel.Port)));
            }
            else
            {
                var address = new Uri(updates.Address);
                if (address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length > 0 || updates.Token.Length == 0)
                {
                    throw new BootstrapFault("clientUpdates.address", "a remote updates service requires HTTPS without URL credentials and a nonempty clientUpdates.token");
                }

                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromSeconds(6));
                if (!await ProbeUpdatesAsync(address, updates.Token, limit.Token).ConfigureAwait(false))
                {
                    throw new BootstrapFault("clientUpdates.address", "remote service failed TLS, authorization, status or writability checks");
                }
            }
        }

        foreach (var item in setup.Validation.ExpectedAddresses)
        {
            if (!_checkedNames.Contains(item.Key))
            {
                throw new BootstrapFault("validation.expectedAddresses", "an expected-address entry does not match a configured service host");
            }
        }

        for (var i = 0; i < routes.Count; i++)
        {
            if (routes.Take(i).Any(r => r.Port == routes[i].Port && string.Equals(Head(r.Path), Head(routes[i].Path), StringComparison.OrdinalIgnoreCase)))
            {
                throw new BootstrapFault(routes[i].Field, "path overlaps another HTTP service on the shared port");
            }
        }

        var planned = enabled.SelectMany(e => e.Settings.Address).Select(a => IPAddress.Parse(a.Split('/')[0])).ToHashSet();
        var addressesOnHost = _host.Addresses.ToHashSet();
        var bound = new HashSet<(IPAddress Address, int Port, ProtocolType Protocol)>();
        foreach (var listener in _listeners.OrderByDescending(l => IsAny(l.Address)))
        {
            if (_listeners.Any(other => other.Protocol == listener.Protocol && other.Port == listener.Port && other.Field != listener.Field
                && !(other.Http && listener.Http)
                && !(listener.Protocol == ProtocolType.Udp && other.Field.StartsWith("endpoints[", StringComparison.Ordinal) && listener.Field.StartsWith("endpoints[", StringComparison.Ordinal))
                && (IsAny(other.Address) || IsAny(listener.Address) || other.Address.Equals(listener.Address))))
            {
                throw new BootstrapFault(listener.Field, "port is assigned to incompatible protocols");
            }

            if (!IPAddress.IsLoopback(listener.Address) && !IsAny(listener.Address) && !addressesOnHost.Contains(listener.Address))
            {
                if (listener.Field != "dns.port" || !planned.Contains(listener.Address))
                {
                    throw new BootstrapFault(listener.Field, "listen address is not assigned to this host");
                }

                continue;
            }

            var probe = listener.Address;
            var any = probe.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any;
            if (bound.Contains((any, listener.Port, listener.Protocol)) || !bound.Add((probe, listener.Port, listener.Protocol)))
            {
                continue;
            }

            try
            {
                _leases.Add(_host.Reserve(probe, listener.Port, listener.Protocol));
            }
            catch (SocketException ex)
            {
                throw new BootstrapFault(listener.Field, "cannot bind " + listener.Protocol + " " + probe + ":" + listener.Port + " (" + ex.SocketErrorCode + "); free the address/port or select another", ex);
            }
        }
    }

    private async Task HostAsync(string host, IReadOnlyList<string> listen, BootstrapValidation validation, string field, CancellationToken ct)
    {
        if (!HostName(host))
        {
            throw new BootstrapFault(field, "expected a DNS name or IP address without a URL, port or path");
        }

        _checkedNames.Add(host);
        var declared = validation.ExpectedAddresses.FirstOrDefault(p => string.Equals(p.Key, host, StringComparison.OrdinalIgnoreCase));
        var expected = declared.Value is not null ? declared.Value.Select(IPAddress.Parse).ToHashSet()
            : listen.Count > 0 ? listen.Select(IPAddress.Parse).ToHashSet() : _host.Addresses.ToHashSet();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        var resolved = await ResolveAsync(host, field, limit.Token).ConfigureAwait(false);
        if (resolved.Length == 0 || resolved.Any(a => !expected.Contains(a)))
        {
            throw new BootstrapFault(field, "DNS returned [" + string.Join(", ", resolved.Select(a => a.ToString())) + "]; expected [" + string.Join(", ", expected.Select(a => a.ToString())) + "]; correct DNS or declare actual public addresses in validation.expectedAddresses for NAT or a proxy");
        }
    }

    private async Task<bool> ProbeDnsAsync(string upstream, int index, CancellationToken ct)
    {
        try
        {
            return await _host.DnsAsync(upstream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or HttpRequestException or System.Security.Authentication.AuthenticationException)
        {
            throw new BootstrapFault("dns.systemServers[" + index + "]", "DNS upstream is unavailable or failed protocol/TLS checks", ex);
        }
    }

    private async Task<bool> ProbeUpdatesAsync(Uri address, string token, CancellationToken ct)
    {
        try
        {
            return await _host.UpdatesAsync(address, token, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or HttpRequestException or System.Text.Json.JsonException or System.Security.Authentication.AuthenticationException)
        {
            throw new BootstrapFault("clientUpdates.address", "remote update service is unavailable or failed TLS/status checks", ex);
        }
    }

    private async Task<IPAddress[]> ResolveAsync(string host, string field, CancellationToken ct)
    {
        try
        {
            return IPAddress.TryParse(host, out var literal) ? [literal] : await _host.ResolveAsync(host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new BootstrapFault(field, "DNS name could not be resolved within the time limit", ex);
        }
    }

    private void Add(string field, IReadOnlyList<string> listen, int port, ProtocolType protocol, bool http)
    {
        foreach (var address in listen.Count > 0 ? listen.Select(IPAddress.Parse) : Socket.OSSupportsIPv6 ? new[] { IPAddress.Any, IPAddress.IPv6Any } : [IPAddress.Any])
        {
            _listeners.Add((field, address, port, protocol, http));
        }
    }

    private static void ValidateOverrides(BootstrapValidation validation)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in validation.ExpectedAddresses)
        {
            if (!names.Add(entry.Key) || !HostName(entry.Key) || entry.Value.Count == 0 || entry.Value.Any(a => !IPAddress.TryParse(a, out var ip) || IsAny(ip)))
            {
                throw new BootstrapFault("validation.expectedAddresses", "specify unique host names and nonempty lists of concrete IP addresses");
            }
        }

        foreach (var path in validation.TrustedCertificates)
        {
            if (!Path.IsPathFullyQualified(path))
            {
                throw new BootstrapFault("validation.trustedCertificates", "an absolute nonempty certificate-file path is required");
            }

            try
            {
                var certificates = new X509Certificate2Collection();
                certificates.ImportFromPemFile(path);
                var count = certificates.Count;
                foreach (var certificate in certificates)
                {
                    certificate.Dispose();
                }

                if (count == 0)
                {
                    throw new BootstrapFault("validation.trustedCertificates", "trust file contains no certificates");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException)
            {
                throw new BootstrapFault("validation.trustedCertificates", "trust file is missing, unreadable or malformed", ex);
            }
        }
    }

    private static void Persistent(string path, string field, bool endpoints = false)
    {
        if (Environment.GetEnvironmentVariable("AMNEZIAGEO_CONTAINER") != "1")
        {
            return;
        }

        var full = Path.GetFullPath(path).TrimEnd('/');
        var roots = endpoints ? new[] { "/var/lib/amneziageo-server", "/etc/amneziageo-server", "/etc/amnezia/amneziawg" }
            : ["/var/lib/amneziageo-server", "/etc/amneziageo-server"];
        if (!roots.Any(root => full == root || full.StartsWith(root + "/", StringComparison.Ordinal)))
        {
            throw new BootstrapFault(field, "container state must remain in the Compose persistent data/settings mounts");
        }
    }

    /// <summary>
    /// Checks a directory with a temporary write.
    /// </summary>
    public static void WritableDirectory(string path, string field)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new BootstrapFault(field, "an absolute directory path is required");
        }

        var probe = Path.Combine(path, ".bootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(path);
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BootstrapFault(field, "directory cannot be created or written", ex);
        }
    }

    /// <summary>
    /// Checks the syntax of a concrete service host.
    /// </summary>
    public static bool HostName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253
        && (IPAddress.TryParse(value, out var ip) ? !IsAny(ip) : value.Split('.').All(p => p.Length is > 0 and <= 63
            && char.IsAsciiLetterOrDigit(p[0]) && char.IsAsciiLetterOrDigit(p[^1]) && p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')));

    private static bool IsAny(IPAddress address) => address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    private static string Head(string path) => path.Trim('/').Split('/')[0];

    public void Dispose()
    {
        foreach (var lease in _leases)
        {
            lease.Dispose();
        }
    }
}
