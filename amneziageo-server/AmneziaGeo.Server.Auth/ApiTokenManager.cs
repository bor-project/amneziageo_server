using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What managing a long lived token produced.
/// </summary>
public enum ApiTokenOutcome
{
    Ok = 0,
    BadName = 1,
    BadLifetime = 2,
    UnknownRole = 3,
    NameTaken = 4,
    Unknown = 5,
}

/// <summary>
/// The token a command produced, and why it was refused.
/// </summary>
public sealed record ApiTokenResult(ApiTokenOutcome Outcome, string Message, MintedToken? Minted)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == ApiTokenOutcome.Ok;

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static ApiTokenResult No(ApiTokenOutcome outcome, string message) => new(outcome, message, null);
}

/// <summary>
/// Mints, lists and revokes long lived tokens by one set of rules.
/// </summary>
public sealed class ApiTokenManager
{
    private readonly RoleManager<AppRole> _roles;

    private readonly IApiTokens _tokens;

    private readonly IAuditLog _audit;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public ApiTokenManager(RoleManager<AppRole> roles, IApiTokens tokens, IAuditLog audit, TimeProvider? time = null)
    {
        _roles = roles;
        _tokens = tokens;
        _audit = audit;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every token with the role it acts by.
    /// </summary>
    public Task<IReadOnlyList<ApiTokenView>> ListAsync(CancellationToken ct) => _tokens.ListAsync(ct);

    /// <summary>
    /// Mints a token that acts by a role and lives a number of days, or has no end when none is given.
    /// </summary>
    public async Task<ApiTokenResult> MintAsync(
        string? name,
        string? role,
        int? days,
        Principal? actor,
        string? address,
        CancellationToken ct)
    {
        var label = name?.Trim() ?? string.Empty;
        if (ApiTokenRules.CheckName(label) is { } shape)
        {
            return ApiTokenResult.No(ApiTokenOutcome.BadName, shape);
        }

        if (ApiTokenRules.CheckDays(days) is { } span)
        {
            return ApiTokenResult.No(ApiTokenOutcome.BadLifetime, span);
        }

        var found = role is { Length: > 0 } ? await _roles.FindByNameAsync(role.Trim()).ConfigureAwait(false) : null;
        if (found is null)
        {
            return ApiTokenResult.No(ApiTokenOutcome.UnknownRole, $"there is no role called '{role}'");
        }

        var held = await _tokens.ListAsync(ct).ConfigureAwait(false);
        if (held.Any(token => string.Equals(token.Name, label, StringComparison.OrdinalIgnoreCase)))
        {
            return ApiTokenResult.No(ApiTokenOutcome.NameTaken, $"the panel already carries a token called '{label}'");
        }

        var now = _time.GetUtcNow();
        var minted = await _tokens.MintAsync(label, found.Name!, Until(now, days), ct).ConfigureAwait(false);
        await NoteAsync(now, actor, "token.mint", minted.Token, address, ct).ConfigureAwait(false);

        return new ApiTokenResult(ApiTokenOutcome.Ok, string.Empty, minted);
    }

    /// <summary>
    /// Revokes a token by its identifier.
    /// </summary>
    public async Task<ApiTokenResult> RevokeAsync(long id, Principal? actor, string? address, CancellationToken ct)
    {
        var held = (await _tokens.ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(token => token.Id == id);
        if (held is null || !await _tokens.RevokeAsync(id, ct).ConfigureAwait(false))
        {
            return ApiTokenResult.No(ApiTokenOutcome.Unknown, $"there is no token {id}");
        }

        await NoteAsync(_time.GetUtcNow(), actor, "token.revoke", held, address, ct).ConfigureAwait(false);

        return new ApiTokenResult(ApiTokenOutcome.Ok, string.Empty, null);
    }

    private static DateTimeOffset? Until(DateTimeOffset now, int? days) => days is { } count ? now.AddDays(count) : null;

    private async Task NoteAsync(
        DateTimeOffset now,
        Principal? actor,
        string action,
        ApiTokenView token,
        string? address,
        CancellationToken ct) =>
        await _audit
            .WriteAsync(
                now,
                actor is { Id: > 0 } ? actor.Id : null,
                actor?.Scheme ?? AuthScheme.HostUser,
                action,
                token.Name,
                $"{token.Id} {token.Role}",
                address,
                ct)
            .ConfigureAwait(false);
}
