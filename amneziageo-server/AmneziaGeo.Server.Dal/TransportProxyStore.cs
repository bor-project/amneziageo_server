using System.Globalization;
using AmneziaGeo.Server.Core.Proxy;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AmneziaGeo.Server.Awg.Config;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Reads the transport proxy catalog.
/// </summary>
public sealed partial class TransportProxyStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<TransportProxyStore>? _logger;

    /// <summary>
    /// ctor
    /// </summary>
    public TransportProxyStore(AppDbContext db, ILogger<TransportProxyStore>? logger = null)
    {
        _db = db;
        _logger = logger;
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
        var byId = selected.Where(proxy => proxy.UsesTls).ToDictionary(proxy => proxy.Id);
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
    /// Resolves the destination policies of the catalog.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, ProxyTargets?>> TargetsAsync(IReadOnlyList<TransportProxyEntity> proxies, CancellationToken ct)
    {
        var policies = proxies.ToDictionary(proxy => proxy.Id, proxy => ProxyTargetRules.Read(proxy.TargetsJson));
        if (!policies.Values.Any(policy => policy is { Local: true }))
        {
            return policies;
        }

        var configs = await new ConfigStore(_db).ListAsync(ct).ConfigureAwait(false);
        var local = configs.Where(config => config.IsEnabled).SelectMany(config =>
            new[] { "127.0.0.1", "::1", config.Host }.Concat(config.Address.Select(address => address.Split('/')[0]))
                .Where(host => ProxyTargetRules.TryHost(host, out _))
                .Select(host => new ProxyDestination(host, config.ListenPort))).ToArray();
        return policies.ToDictionary(pair => pair.Key, pair => ProxyTargetRules.Expand(pair.Value, local));
    }

    /// <summary>
    /// Resolves the destination policy of a proxy.
    /// </summary>
    public async Task<ProxyTargets?> TargetsAsync(TransportProxyEntity proxy, CancellationToken ct) =>
        (await TargetsAsync([proxy], ct).ConfigureAwait(false))[proxy.Id];

    /// <summary>
    /// Reports whether a proxy uses a port.
    /// </summary>
    public Task<bool> ServesAsync(int port, CancellationToken ct) =>
        _db.TransportProxies.AnyAsync(proxy => proxy.IsEnabled && proxy.Hosted && (proxy.Transport == "websocket" || proxy.Transport == "http" || (proxy.Transport == "xhttp" && proxy.ProtocolVersion == 2)) && (proxy.ListenPort == 0 ? proxy.Port : proxy.ListenPort) == port, ct);

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
        held.UseCertificate = draft.UseCertificate;
        held.HttpMethod = draft.HttpMethod;
        held.Hosted = draft.Hosted;
        held.ProtocolVersion = draft.ProtocolVersion;
        held.Path = draft.Path.Trim().Trim('/');
        held.Certificate = draft.Certificate.Trim();
        held.CertificateKey = draft.CertificateKey.Trim();
        held.ServerName = draft.ServerName.Trim();
        held.Pin = draft.Pin.Trim();
        held.TargetsJson = draft.TargetsJson;
        if (held.AllowAnonymous && !draft.AllowAnonymous)
        {
            held.AccessVersion++;
        }

        held.AllowAnonymous = draft.AllowAnonymous;
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
        var credentials = clients.Select(id => Credential(proxy, id)).Concat(TokenCredentials(proxy));
        return [.. (proxy.AllowAnonymous ? credentials.Append(ProxyAccess.AnonymousCredential) : credentials).Distinct(StringComparer.Ordinal)];
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

    /// <summary>
    /// Identifies persisted GET profiles of the prototype.
    /// </summary>
    public async Task AdoptHttpGetAsync(string directory, CancellationToken ct)
    {
        var legacy = await _db.TransportProxies.Where(proxy => proxy.Transport == "xhttp" && proxy.ProtocolVersion == 0)
            .ToListAsync(ct).ConfigureAwait(false);
        var clients = await _db.Clients.AsNoTracking().Select(client => client.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var proxy in legacy)
        {
            var file = Path.Combine(directory, $"xhttp-{proxy.Id}.json");
            if (!File.Exists(file) || new FileInfo(file).Length > 1_048_576)
            {
                continue;
            }

            try
            {
                using var profile = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
                var known = clients.Select(id => Credential(proxy, id)).ToHashSet(StringComparer.Ordinal);
                if (!profile.RootElement.TryGetProperty("httpGet", out var transport)
                    || !transport.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 2
                    || !transport.TryGetProperty("path", out var path) || path.GetString() != "/" + proxy.Path.Trim('/') + "/"
                    || !transport.TryGetProperty("credentials", out var credentials) || credentials.ValueKind != JsonValueKind.Array
                    || credentials.GetArrayLength() == 0 || credentials.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String || !known.Contains(value.GetString()!)))
                {
                    continue;
                }

                proxy.ProtocolVersion = 2;
                proxy.HttpMethod = "GET";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                _logger?.LogWarning(ex, "The legacy proxy profile {ProxyId} could not be identified", proxy.Id);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<long> Numbers(string text) => [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => long.TryParse(part.Trim(), out var id) ? id : 0).Where(id => id > 0).Distinct()];

    /// <summary>
    /// Derives the transport credential of a client.
    /// </summary>
    public static string Credential(TransportProxyEntity proxy, long clientId)
    {
        var version = proxy.IsHttp ? "v2" : "v1";
        var name = "transport-proxy-" + version + ":" + clientId.ToString(CultureInfo.InvariantCulture);
        var digest = HMACSHA256.HashData(Convert.FromBase64String(proxy.Secret), Encoding.UTF8.GetBytes(name));
        return proxy.IsHttp
            ? new Guid(digest.AsSpan(0, 16)).ToString("D")
            : Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
