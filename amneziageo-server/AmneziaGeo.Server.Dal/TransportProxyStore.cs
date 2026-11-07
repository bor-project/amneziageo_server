using System.Globalization;
using AmneziaGeo.Server.Awg.Config;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads the transport proxy catalog.
/// </summary>
public sealed class TransportProxyStore
{
    private readonly AppDbContext _db;

    /// <summary>
    /// ctor
    /// </summary>
    public TransportProxyStore(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the identity of the catalog.
    /// </summary>
    public async Task<string> SourceAsync(CancellationToken ct) =>
        (await IdentityAsync(ct).ConfigureAwait(false)).Source;

    /// <summary>
    /// Returns the signing identity of the catalog.
    /// </summary>
    public Task<ProxySourceEntity> IdentityAsync(CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var held = await _db.ProxySources.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 1, ct).ConfigureAwait(false);
        var identity = held ?? new ProxySourceEntity { Id = 1, Source = Guid.NewGuid().ToString("D") };
        if (!Guid.TryParseExact(identity.Source, "D", out var source))
        {
            throw new InvalidOperationException("the proxy catalog source is invalid");
        }

        if (identity.IdentityVersion == 0 && identity.IdentityKey.Length == 0)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            identity.IdentityKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
            identity.IdentityVersion = 1;
            if (held is null)
            {
                _db.ProxySources.Add(identity);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await _db.ProxySources.Where(row => row.Id == 1).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.IdentityKey, identity.IdentityKey)
                    .SetProperty(row => row.IdentityVersion, identity.IdentityVersion), ct).ConfigureAwait(false);
            }
        }

        if (identity.IdentityVersion != 1)
        {
            throw new InvalidOperationException("the proxy catalog identity version is invalid");
        }

        CheckIdentityKey(identity.IdentityKey);
        return new ProxySourceEntity
        {
            Id = identity.Id, Source = source.ToString("D"),
            IdentityKey = identity.IdentityKey, IdentityVersion = identity.IdentityVersion,
        };
    }, ct);

    private static void CheckIdentityKey(string text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text);
            try
            {
                using var key = ECDsa.Create();
                key.ImportPkcs8PrivateKey(bytes, out var read);
                if (read != bytes.Length || key.KeySize != 256
                    || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                {
                    throw new InvalidOperationException("the proxy catalog identity key is invalid");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("the proxy catalog identity key is invalid", ex);
        }
    }

    /// <summary>
    /// Returns the enabled proxies selected by a template.
    /// </summary>
    public async Task<IReadOnlyList<TransportProxyEntity>> SelectedAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var selected = await _db.TransportProxies.AsNoTracking()
            .Where(proxy => proxy.IsEnabled && ids.Contains(proxy.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = selected.ToDictionary(proxy => proxy.Id);
        return [.. ids.Distinct().Where(byId.ContainsKey).Select(id => byId[id])];
    }

    /// <summary>
    /// Returns all registered proxies.
    /// </summary>
    public async Task<IReadOnlyList<TransportProxyEntity>> ListAsync(CancellationToken ct) =>
        await _db.TransportProxies.AsNoTracking().OrderBy(proxy => proxy.Id).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Returns a registered proxy.
    /// </summary>
    public Task<TransportProxyEntity?> FindAsync(long id, CancellationToken ct) =>
        _db.TransportProxies.AsNoTracking().SingleOrDefaultAsync(proxy => proxy.Id == id, ct);

    /// <summary>
    /// Reports whether a proxy uses a port.
    /// </summary>
    public Task<bool> ServesAsync(int port, CancellationToken ct) =>
        _db.TransportProxies.AnyAsync(proxy => proxy.IsEnabled && (proxy.Transport == "websocket" || proxy.Transport == "xhttp") && (proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort) == port, ct);

    /// <summary>
    /// Returns the proxy that owns a path.
    /// </summary>
    public async Task<TransportProxyEntity?> UnderAsync(int? port, string path, long? legacy, CancellationToken ct)
    {
        var proxies = await _db.TransportProxies.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return proxies.FirstOrDefault(proxy => (legacy is null || proxy.LegacyConfigId != legacy)
            && (port is null || (proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort) == port) && string.Equals(proxy.Path, path.Trim('/'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stores a proxy and its transport secret.
    /// </summary>
    public async Task<TransportProxyEntity> SaveAsync(long id, TransportProxyEntity draft, CancellationToken ct)
    {
        var held = id == 0 ? new TransportProxyEntity() : await _db.TransportProxies.SingleAsync(proxy => proxy.Id == id, ct).ConfigureAwait(false);
        held.Name = draft.Name.Trim();
        held.Transport = draft.Transport;
        held.Host = draft.Host.Trim().Trim('[', ']');
        held.Port = draft.Port;
        held.ListenPort = draft.ListenPort;
        held.OriginHttp = draft.OriginHttp;
        held.Path = draft.Path.Trim().Trim('/');
        held.Certificate = draft.Certificate.Trim();
        held.CertificateKey = draft.CertificateKey.Trim();
        held.ServerName = draft.ServerName.Trim();
        held.Pin = draft.Pin.Trim();
        held.TargetsJson = draft.TargetsJson;
        held.IsEnabled = draft.IsEnabled;
        held.Opened = draft.Opened;
        held.UpdatedUtc = DateTimeOffset.UtcNow;
        if (id == 0)
        {
            held.Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _db.TransportProxies.Add(held);
        }

        if (held.LegacyConfigId is { } legacy
            && await _db.Configs.SingleOrDefaultAsync(config => config.Id == legacy, ct).ConfigureAwait(false) is { } endpoint)
        {
            endpoint.WebSocket = held.IsEnabled;
            endpoint.UpdatedUtc = held.UpdatedUtc;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return held;
    }

    /// <summary>
    /// Removes a proxy and its template references.
    /// </summary>
    public async Task RemoveAsync(long id, CancellationToken ct)
    {
        await _db.AloneAsync(async () =>
        {
            var held = await _db.TransportProxies.SingleAsync(proxy => proxy.Id == id, ct).ConfigureAwait(false);
            if (held.LegacyConfigId is { } legacy
                && await _db.Configs.SingleOrDefaultAsync(config => config.Id == legacy, ct).ConfigureAwait(false) is { } endpoint)
            {
                endpoint.WebSocket = false;
                endpoint.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            _db.TransportProxies.Remove(held);
            foreach (var template in await _db.Templates.ToListAsync(ct).ConfigureAwait(false))
            {
                template.ProxyIds = string.Join(", ", Numbers(template.ProxyIds).Where(one => one != id));
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the clients authorized by their current template.
    /// </summary>
    public async Task<IReadOnlyList<string>> CredentialsAsync(TransportProxyEntity proxy, CancellationToken ct)
    {
        var templates = await _db.Templates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var selected = templates.Where(template => Numbers(template.ProxyIds).Contains(proxy.Id)).Select(template => template.Id).ToArray();
        var clients = await _db.Clients.AsNoTracking()
            .Where(client => client.IsEnabled && client.TemplateId != null && selected.Contains(client.TemplateId.Value))
            .Select(client => client.Id).ToListAsync(ct).ConfigureAwait(false);
        return [.. clients.Select(id => Credential(proxy, id))];
    }

    /// <summary>
    /// Copies legacy WebSocket settings into the catalog.
    /// </summary>
    public Task MigrateLegacyAsync(CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var configs = await _db.Configs.Where(config => config.WebSocket).OrderBy(config => config.Id).ToListAsync(ct).ConfigureAwait(false);
        var proxies = await _db.TransportProxies.ToListAsync(ct).ConfigureAwait(false);
        var markers = await _db.Seeds.Where(seed => seed.Name.StartsWith("proxy-legacy-")).Select(seed => seed.Name).ToListAsync(ct).ConfigureAwait(false);
        foreach (var config in configs.Where(config => !proxies.Any(proxy => proxy.LegacyConfigId == config.Id)
            && !markers.Contains("proxy-legacy-" + config.Id.ToString(CultureInfo.InvariantCulture))))
        {
            var name = "WebSocket " + config.Name;
            for (var suffix = 2; proxies.Any(proxy => proxy.Name == name); suffix++)
            {
                name = "WebSocket " + config.Name + " " + suffix.ToString(CultureInfo.InvariantCulture);
            }

            var proxy = new TransportProxyEntity
            {
                Name = name, LegacyConfigId = config.Id, Host = config.Host,
                Port = config.ServicesPort == 0 ? config.ListenPort : config.ServicesPort,
                Path = config.WebSocketPath.Length > 0 ? config.WebSocketPath : ConfigServices.OldPath,
                IsEnabled = config.IsEnabled, Opened = config.Opened, Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                UpdatedUtc = config.UpdatedUtc,
            };
            _db.TransportProxies.Add(proxy);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            proxies.Add(proxy);
            _db.Seeds.Add(new SeedEntity { Name = "proxy-legacy-" + config.Id.ToString(CultureInfo.InvariantCulture), Version = 1, UpdatedUtc = DateTimeOffset.UtcNow });
            var ids = await _db.Clients.Where(client => client.ConfigId == config.Id && client.TemplateId != null)
                .Select(client => client.TemplateId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
            foreach (var template in await _db.Templates.Where(template => ids.Contains(template.Id)).ToListAsync(ct).ConfigureAwait(false))
            {
                template.ProxyIds = string.Join(", ", Numbers(template.ProxyIds).Append(proxy.Id).Distinct());
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }, ct);

    private static IReadOnlyList<long> Numbers(string text) => [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => long.TryParse(part.Trim(), out var id) ? id : 0).Where(id => id > 0).Distinct()];

    /// <summary>
    /// Derives the transport credential of a client.
    /// </summary>
    public static string Credential(TransportProxyEntity proxy, long clientId)
    {
        var name = "transport-proxy-v1:" + clientId.ToString(CultureInfo.InvariantCulture);
        var digest = HMACSHA256.HashData(Convert.FromBase64String(proxy.Secret), Encoding.UTF8.GetBytes(name));
        return proxy.Transport == "xhttp"
            ? new Guid(digest.AsSpan(0, 16)).ToString("D")
            : Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
