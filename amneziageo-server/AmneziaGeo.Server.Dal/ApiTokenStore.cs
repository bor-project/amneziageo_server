using System.Buffers.Text;
using System.Security.Cryptography;
using AmneziaGeo.Server.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Keeps long lived tokens in the database as hashes.
/// </summary>
public sealed class ApiTokenStore : IApiTokens
{
    private const int SecretLength = 32;

    private static readonly TimeSpan _touchEvery = TimeSpan.FromMinutes(1);

    private readonly AppDbContext _db;

    private readonly RoleManager<AppRole> _roles;

    private readonly AccessResolver _access;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ApiTokenStore(AppDbContext db, RoleManager<AppRole> roles, AccessResolver access, TimeProvider? time = null)
    {
        _db = db;
        _roles = roles;
        _access = access;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Mints a token that acts by a role, returning the only copy of its secret.
    /// </summary>
    public async Task<MintedToken> MintAsync(string name, string role, DateTimeOffset? expires, CancellationToken ct)
    {
        var secret = ApiTokenRules.Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretLength));
        var token = new ApiTokenEntity
        {
            Name = name,
            Role = role,
            TokenHash = RefreshTokenStore.Fingerprint(secret),
            CreatedUtc = _time.GetUtcNow(),
            ExpiresUtc = expires,
        };

        _db.ApiTokens.Add(token);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new MintedToken(View(token), secret);
    }

    /// <summary>
    /// Resolves a presented token, returning null when it is unknown, expired or its role is gone.
    /// </summary>
    public async Task<Principal?> ResolveAsync(string token, string? address, CancellationToken ct)
    {
        if (!ApiTokenRules.Looks(token))
        {
            return null;
        }

        var hash = RefreshTokenStore.Fingerprint(token);
        var found = await _db.ApiTokens.FirstOrDefaultAsync(item => item.TokenHash == hash, ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        if (found is null || Expired(found, now))
        {
            return null;
        }

        var role = await _roles.FindByNameAsync(found.Role).ConfigureAwait(false);
        if (role is null)
        {
            return null;
        }

        if (Stale(found, now, address))
        {
            found.LastUsedUtc = now;
            found.LastAddress = address;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var scopes = await _access.ScopesAsync(role).ConfigureAwait(false);

        return new Principal(0, found.Name, AuthScheme.ApiToken, 0, scopes, role.Name!);
    }

    /// <summary>
    /// Returns every token with the role it acts by.
    /// </summary>
    public async Task<IReadOnlyList<ApiTokenView>> ListAsync(CancellationToken ct)
    {
        var found = await _db.ApiTokens.OrderBy(item => item.Id).ToListAsync(ct).ConfigureAwait(false);

        return [.. found.Select(View)];
    }

    /// <summary>
    /// Returns how many tokens act by a role.
    /// </summary>
    public async Task<int> CountAsync(string role, CancellationToken ct) =>
        await _db.ApiTokens.CountAsync(item => item.Role == role, ct).ConfigureAwait(false);

    /// <summary>
    /// Revokes a token by its identifier, returning false when there is none.
    /// </summary>
    public async Task<bool> RevokeAsync(long tokenId, CancellationToken ct)
    {
        var found = await _db.ApiTokens.FirstOrDefaultAsync(item => item.Id == tokenId, ct).ConfigureAwait(false);
        if (found is null)
        {
            return false;
        }

        _db.ApiTokens.Remove(found);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return true;
    }

    private static bool Expired(ApiTokenEntity token, DateTimeOffset now) => token.ExpiresUtc is { } until && until <= now;

    private static bool Stale(ApiTokenEntity token, DateTimeOffset now, string? address) =>
        token.LastUsedUtc is not { } seen
        || now - seen >= _touchEvery
        || !string.Equals(token.LastAddress, address, StringComparison.Ordinal);

    private static ApiTokenView View(ApiTokenEntity token) => new(
        token.Id,
        token.Name,
        token.Role,
        token.CreatedUtc,
        token.ExpiresUtc,
        token.LastUsedUtc,
        token.LastAddress);
}
