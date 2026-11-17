using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Routing.Udp;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Runs QUIC listeners with destination and credential restrictions.
/// </summary>
public sealed class QuicHost(string directory, string executable, ILogger logger, UdpPorts? udp = null) : IAsyncDisposable
{
    private readonly Dictionary<int, (string Configuration, Process Process)> _children = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string Rules(IReadOnlyList<ProxyServiceEntry> proxies)
    {
        var text = new StringBuilder("restrictions:\n");
        foreach (var proxy in proxies)
        {
            var rule = ProxyFile.Whitelist(0, proxy.Path, proxy.Targets)["restrictions:\n".Length..];
            var credentials = proxy.Credentials.Count == 0 ? @"^\x00$" : "^Bearer (?:" + string.Join('|', proxy.Credentials.Select(Regex.Escape)) + ")$";
            rule = rule.Replace("name: amneziageo", "name: proxy-" + proxy.Id, StringComparison.Ordinal);
            if (!proxy.AllowAnonymous)
            {
                rule = rule.Replace("    allow:", "      - !Authorization '" + credentials + "'\n    allow:", StringComparison.Ordinal);
            }
            text.Append(rule);
        }

        return text.ToString();
    }

    public async Task<IReadOnlyDictionary<long, string>> ApplyAsync(IReadOnlyList<ProxyServiceEntry> proxies, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var faults = new Dictionary<long, string>();
            var ports = proxies.GroupBy(one => one.ListenPort == 0 ? one.Port : one.ListenPort).ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var port in _children.Keys.Where(port => !ports.ContainsKey(port)).ToArray())
            {
                await StopAsync(port).ConfigureAwait(false);
            }

            foreach (var (port, entries) in ports)
            {
                var first = entries[0];
                var backend = udp?.Quic.GetValueOrDefault(port) ?? 0;
                var address = backend > 0 ? "127.0.0.1:" + backend : "[::]:" + port;
                var configuration = address + "\n" + Rules(entries) + "\n" + first.Certificate + "\n" + first.CertificateKey;
                if (entries.Any(one => ProxySecurity.Check(ProxySecurity.Of(one), logger: logger) is not null || one.Certificate.Length == 0
                    || !SameCertificate(one.Certificate, one.CertificateKey, first.Certificate, first.CertificateKey, logger)))
                {
                    await StopAsync(port).ConfigureAwait(false);
                    foreach (var entry in entries)
                    {
                        faults[entry.Id] = "quic-port-requires-one-certificate";
                    }

                    continue;
                }

                if (_children.TryGetValue(port, out var held) && held.Configuration == configuration && !held.Process.HasExited)
                {
                    continue;
                }

                await StopAsync(port).ConfigureAwait(false);
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Directory.CreateDirectory(directory);
                    }
                    else
                    {
                        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    var file = Path.Combine(directory, "quic-" + port + ".yaml");
                    await File.WriteAllTextAsync(file, Rules(entries), ct).ConfigureAwait(false);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    var process = new Process { StartInfo = new ProcessStartInfo(executable)
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    } };
                    foreach (var argument in new[] { "server", "wts://" + address, "--enable-webtransport", "--restrict-config", file,
                        "--tls-certificate", first.Certificate, "--tls-private-key", first.CertificateKey.Length > 0 ? first.CertificateKey : first.Certificate })
                    {
                        process.StartInfo.ArgumentList.Add(argument);
                    }

                    if (!process.Start())
                    {
                        process.Dispose();
                        throw new IOException("The QUIC listener did not start.");
                    }

                    _children[port] = (configuration, process);
                    _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
                    _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
                    await Task.Delay(250, ct).ConfigureAwait(false);
                    if (process.HasExited)
                    {
                        throw new IOException("The QUIC listener exited with code " + process.ExitCode + ".");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    logger.LogWarning(ex, "The QUIC listener on UDP port {Port} did not start", port);
                    await StopAsync(port).ConfigureAwait(false);
                    foreach (var entry in entries)
                    {
                        faults[entry.Id] = "quic-listener-unavailable";
                    }
                }
            }

            return faults;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool SameCertificate(string chain, string key, string otherChain, string otherKey, ILogger? logger = null)
    {
        if (chain == otherChain && key == otherKey)
        {
            return true;
        }

        try
        {
            using var left = X509Certificate2.CreateFromPem(WebCertificate.Read(chain), WebCertificate.Read(key.Length > 0 ? key : chain));
            using var right = X509Certificate2.CreateFromPem(WebCertificate.Read(otherChain), WebCertificate.Read(otherKey.Length > 0 ? otherKey : otherChain));
            return CryptographicOperations.FixedTimeEquals(left.GetCertHash(HashAlgorithmName.SHA256), right.GetCertHash(HashAlgorithmName.SHA256));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).LogWarning(ex, "The QUIC listener certificates could not be compared");
            return false;
        }
    }

    private async Task StopAsync(int port)
    {
        if (_children.Remove(port, out var held))
        {
            using var process = held.Process;
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var port in _children.Keys.ToArray())
            {
                await StopAsync(port).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
