using System.Text.Json;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxies;

public sealed record ProxyCatalog(int Version, IReadOnlyList<ProxyServiceEntry> Proxies)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Check(ProxyCatalog catalog, bool preserveInvalidRecords = false)
    {
        if (catalog.Version != 1 || catalog.Proxies is null || catalog.Proxies.Any(one => one is null) || catalog.Proxies.Count > 128 || catalog.Proxies.Select(one => one.Id).Distinct().Count() != catalog.Proxies.Count)
        {
            throw new InvalidDataException("Invalid proxy catalog version or identifiers.");
        }

        foreach (var entry in catalog.Proxies)
        {
            if (entry.Name is null || entry.Host is null || entry.Transport is null || entry.Path is null || entry.HttpMethod is null
                || entry.Certificate is null || entry.CertificateKey is null || entry.ServerName is null || entry.Pin is null || entry.Credentials is null || entry.Credentials.Any(value => value is null)
                || entry.Id <= 0 || entry.Credentials.Count == 0 && !entry.AllowAnonymous || entry.Credentials.Count > 4096 || entry.Credentials.Any(value => value.Length is 0 or > 256 || !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            {
                throw new InvalidDataException("The proxy requires an identifier and credentials.");
            }

            var identities = ProxyAccess.Credentials(entry.Credentials, entry.AllowAnonymous).Select(ProxyAccess.HttpIdentity).ToHashSet(StringComparer.Ordinal);
            if (entry.BearerIds is { } bearerIds && (bearerIds.Count > 64 || bearerIds.Any(id => id is null || id.Length != 64 || !identities.Contains(id))))
            {
                throw new InvalidDataException("The bearer identity does not belong to a proxy credential.");
            }

            var fault = ProxyRules.Check(new TransportProxyEntity
            {
                Name = entry.Name, Transport = entry.Transport, Host = entry.Host, Port = entry.Port, Path = entry.Path,
                ListenPort = entry.ListenPort, UseCertificate = entry.UseCertificate, OriginHttp = entry.OriginHttp,
                HttpMethod = entry.HttpMethod, ProtocolVersion = entry.ProtocolVersion, TargetsJson = ProxyTargetRules.Write(entry.Targets),
                ServerName = entry.ServerName, Pin = entry.Pin,
            });
            if (fault is not null && !preserveInvalidRecords)
            {
                throw new InvalidDataException(fault.Error);
            }
        }

        if (catalog.Proxies.GroupBy(one => (one.ListenPort == 0 ? one.Port : one.ListenPort, one.Path.ToLowerInvariant())).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("A proxy path is repeated on the listener port.");
        }
    }

    public static async Task WriteAsync<T>(string file, T value, CancellationToken ct)
    {
        var path = Path.GetFullPath(file);
        var directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            var stream = new FileStream(temporary, options);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, value, Json, ct).ConfigureAwait(false);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

public sealed record ProxyServiceEntry(long Id, string Name, string Transport, string Host, int Port, string Path,
    IReadOnlyList<string> Credentials, bool UseCertificate = true, string HttpMethod = "GET", int ProtocolVersion = 0,
    int ListenPort = 0, string Certificate = "", string CertificateKey = "", bool OriginHttp = false, ProxyTargets? Targets = null,
    string ServerName = "", string Pin = "", bool AllowAnonymous = false, IReadOnlyList<string>? BearerIds = null);
