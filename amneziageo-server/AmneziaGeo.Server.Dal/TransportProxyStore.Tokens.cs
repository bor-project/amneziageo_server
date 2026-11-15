using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Core.Proxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Dal;

public sealed record ProxyTokenInfo(string Id, string Name, DateTimeOffset CreatedUtc);

public sealed record MintedProxyToken(ProxyTokenInfo Token, string Secret);

internal sealed record StoredProxyToken(string Id, string Name, DateTimeOffset CreatedUtc, string Encrypted);

public sealed partial class TransportProxyStore
{
    private static readonly JsonSerializerOptions TokenJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Lists proxy token metadata.
    /// </summary>
    public IReadOnlyList<ProxyTokenInfo> Tokens(TransportProxyEntity proxy) =>
        [.. ReadTokens(proxy).Select(token => new ProxyTokenInfo(token.Id, token.Name, token.CreatedUtc))];

    /// <summary>
    /// Identifies HTTP credentials requiring bearer headers.
    /// </summary>
    public IReadOnlyList<string> BearerIds(TransportProxyEntity proxy) =>
        [.. TokenCredentials(proxy).Select(ProxyAccess.HttpIdentity).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Creates a proxy access token.
    /// </summary>
    public Task<(MintedProxyToken? Minted, string Error)> MintAsync(long proxyId, string name, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var proxy = await _db.TransportProxies.SingleOrDefaultAsync(proxy => proxy.Id == proxyId, ct).ConfigureAwait(false);
        if (proxy is null)
        {
            return ((MintedProxyToken?)null, "unknown-proxy");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 64 || trimmed.Any(char.IsControl))
        {
            return ((MintedProxyToken?)null, "bad-proxy-token-name");
        }

        var held = ReadTokens(proxy);
        if (held.Count >= 64)
        {
            return ((MintedProxyToken?)null, "proxy-token-limit");
        }

        if (held.Any(token => string.Equals(token.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return ((MintedProxyToken?)null, "proxy-token-name-taken");
        }

        var secret = Guid.NewGuid().ToString("D");
        var info = new ProxyTokenInfo(Guid.NewGuid().ToString("D"), trimmed, DateTimeOffset.UtcNow);
        var encrypted = Encrypt(proxy, info.Id, secret);
        proxy.TokensJson = JsonSerializer.Serialize(held.Append(new StoredProxyToken(info.Id, info.Name, info.CreatedUtc, encrypted)), TokenJson);
        proxy.UpdatedUtc = info.CreatedUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ((MintedProxyToken?)new MintedProxyToken(info, secret), string.Empty);
    }, ct);

    /// <summary>
    /// Revokes a proxy access token.
    /// </summary>
    public Task<bool> RevokeAsync(long proxyId, string tokenId, CancellationToken ct) => _db.AloneAsync(async () =>
    {
        var proxy = await _db.TransportProxies.SingleOrDefaultAsync(proxy => proxy.Id == proxyId, ct).ConfigureAwait(false);
        if (proxy is null)
        {
            return false;
        }

        var held = ReadTokens(proxy);
        var kept = held.Where(token => token.Id != tokenId).ToArray();
        if (kept.Length == held.Count)
        {
            return false;
        }

        proxy.TokensJson = JsonSerializer.Serialize(kept, TokenJson);
        proxy.AccessVersion++;
        proxy.UpdatedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }, ct);

    /// <summary>
    /// Identifies the current manual access policy.
    /// </summary>
    public string AccessRevision(TransportProxyEntity proxy)
    {
        return proxy.AccessVersion == 0 ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proxy.AccessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private IReadOnlyList<StoredProxyToken> ReadTokens(TransportProxyEntity proxy)
    {
        if (proxy.TokensJson.Length == 0)
        {
            return [];
        }

        try
        {
            var tokens = JsonSerializer.Deserialize<StoredProxyToken[]>(proxy.TokensJson, TokenJson);
            if (tokens is null || tokens.Length > 64 || tokens.Any(token => token is null || !Guid.TryParseExact(token.Id, "D", out _)
                || token.Name is null || token.Encrypted is null) || tokens.Select(token => token.Id).Distinct().Count() != tokens.Length)
            {
                throw new JsonException("Invalid proxy tokens.");
            }

            return tokens;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "The access tokens of proxy {ProxyId} could not be read", proxy.Id);
            return [];
        }
    }

    private IReadOnlyList<string> TokenCredentials(TransportProxyEntity proxy)
    {
        var credentials = new List<string>();
        foreach (var token in ReadTokens(proxy))
        {
            try
            {
                var packed = Convert.FromBase64String(token.Encrypted);
                var clear = new byte[packed.Length - 28];
                try
                {
                    var key = TokenKey(proxy);
                    try
                    {
                        using var cipher = new AesGcm(key, 16);
                        cipher.Decrypt(packed.AsSpan(0, 12), packed.AsSpan(28), packed.AsSpan(12, 16), clear, Encoding.UTF8.GetBytes(token.Id));
                        var value = Encoding.UTF8.GetString(clear);
                        if (Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty)
                        {
                            credentials.Add(value);
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(key);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(clear);
                }
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or OverflowException)
            {
                _logger?.LogWarning(ex, "Token {TokenId} of proxy {ProxyId} could not be opened", token.Id, proxy.Id);
            }
        }

        return credentials;
    }

    private static string Encrypt(TransportProxyEntity proxy, string tokenId, string secret)
    {
        var key = TokenKey(proxy);
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var packed = new byte[28 + clear.Length];
            RandomNumberGenerator.Fill(packed.AsSpan(0, 12));
            using var cipher = new AesGcm(key, 16);
            cipher.Encrypt(packed.AsSpan(0, 12), clear, packed.AsSpan(28), packed.AsSpan(12, 16), Encoding.UTF8.GetBytes(tokenId));
            return Convert.ToBase64String(packed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] TokenKey(TransportProxyEntity proxy)
    {
        var root = Convert.FromBase64String(proxy.Secret);
        try
        {
            return HMACSHA256.HashData(root, "transport-proxy-token-storage-v1"u8);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
        }
    }
}
