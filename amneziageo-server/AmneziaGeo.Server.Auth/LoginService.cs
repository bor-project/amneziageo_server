using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What a login attempt produced.
/// </summary>
public enum LoginOutcome
{
    Ok = 0,
    UnknownUser = 1,
    WrongPassword = 2,
    NoPassword = 3,
    Disabled = 4,
    Locked = 5,
    NoHostUser = 6,
    NameTaken = 7,
    Unsupported = 8,
    HostLoginOff = 9,
    NoRole = 10,
    RefreshUnknown = 11,
    RefreshExpired = 12,
    RefreshReplayed = 13,
    SessionEnded = 14,
}

/// <summary>
/// The tokens a login hands back.
/// </summary>
public sealed record LoginResult(
    LoginOutcome Outcome,
    string? Access,
    string? Refresh,
    Principal? Principal,
    bool MustChangePassword = false)
{
    /// <summary>
    /// Tells whether the attempt produced an access token.
    /// </summary>
    public bool IsOk => Outcome == LoginOutcome.Ok;
}

/// <summary>
/// Turns a password or a host account into an access and a refresh token.
/// </summary>
public sealed class LoginService
{
    private readonly UserManager<AppUser> _users;

    private readonly AccountManager _accounts;

    private readonly AccessResolver _access;

    private readonly IRefreshTokens _refreshTokens;

    private readonly ITokenIssuer _issuer;

    private readonly IAuditLog _audit;

    private readonly AuthOptions _options;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public LoginService(
        UserManager<AppUser> users,
        AccountManager accounts,
        AccessResolver access,
        IRefreshTokens refreshTokens,
        ITokenIssuer issuer,
        IAuditLog audit,
        AuthOptions options,
        TimeProvider? time = null)
    {
        _users = users;
        _accounts = accounts;
        _access = access;
        _refreshTokens = refreshTokens;
        _issuer = issuer;
        _audit = audit;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Signs a panel account in by its password.
    /// </summary>
    public async Task<LoginResult> PasswordAsync(string name, string password, string? address, string? agent, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            await NoteAsync(now, null, AuthScheme.Password, "login.unknown", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.UnknownUser);
        }

        if (!user.IsEnabled)
        {
            await NoteAsync(now, user.Id, AuthScheme.Password, "login.disabled", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.Disabled);
        }

        if (!await _users.HasPasswordAsync(user).ConfigureAwait(false))
        {
            await NoteAsync(now, user.Id, AuthScheme.Password, "login.notset", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.NoPassword);
        }

        if (user.LockoutEnd is { } until && until > now)
        {
            await NoteAsync(now, user.Id, AuthScheme.Password, "login.locked", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.Locked);
        }

        if (!await _users.CheckPasswordAsync(user, password).ConfigureAwait(false))
        {
            var outcome = await FailAsync(user, now).ConfigureAwait(false);
            await NoteAsync(now, user.Id, AuthScheme.Password, Action("login", outcome), name, address, ct).ConfigureAwait(false);

            return Failed(outcome);
        }

        await PassedAsync(user).ConfigureAwait(false);

        if (user.MustChangePassword)
        {
            await NoteAsync(now, user.Id, AuthScheme.Password, "login.mustchange", name, address, ct).ConfigureAwait(false);
            var limited = new Principal(user.Id, user.Name, AuthScheme.Password, 0, Only(Scopes.ChangePassword));

            return new LoginResult(LoginOutcome.Ok, _issuer.Issue(limited, now), null, limited, MustChangePassword: true);
        }

        return await IssueAsync(user, AuthScheme.Password, address, agent, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Signs the host account the process runs as in, registering it when the host maps it to a role.
    /// </summary>
    public async Task<LoginResult> HostAsync(string? address, string? agent, CancellationToken ct)
    {
        if (_options.HostLogin == HostLogin.Off)
        {
            return Failed(LoginOutcome.HostLoginOff);
        }

        if (!LocalUsers.IsSupported)
        {
            return Failed(LoginOutcome.Unsupported);
        }

        var user = LocalUsers.Current();
        if (user is null)
        {
            return Failed(LoginOutcome.NoHostUser);
        }

        var now = _time.GetUtcNow();
        var granted = HostRole(user);
        var record = FindByHostUser(user.Name);

        if (record is null)
        {
            if (await _users.FindByNameAsync(user.Name).ConfigureAwait(false) is not null)
            {
                return Failed(LoginOutcome.NameTaken);
            }

            if (_options.HostLogin == HostLogin.Strict && granted is null)
            {
                await NoteAsync(now, null, AuthScheme.HostUser, "login.norole", user.Name, address, ct).ConfigureAwait(false);

                return Failed(LoginOutcome.NoRole);
            }

            var added = await _accounts.RegisterHostUserAsync(user.Name, user.Uid, granted, ct).ConfigureAwait(false);
            if (!added.IsOk)
            {
                return Failed(LoginOutcome.NameTaken);
            }

            record = added.Record!;
            await NoteAsync(now, record.Id, AuthScheme.HostUser, "login.register", user.Name, address, ct).ConfigureAwait(false);
        }
        else
        {
            await SeenAsync(record, user.Uid, now).ConfigureAwait(false);
            await SyncRoleAsync(record, granted, ct).ConfigureAwait(false);

            if (_options.HostLogin == HostLogin.Strict
                && (await _access.ScopesAsync(record).ConfigureAwait(false)).Count == 0)
            {
                await NoteAsync(now, record.Id, AuthScheme.HostUser, "login.norole", user.Name, address, ct).ConfigureAwait(false);

                return Failed(LoginOutcome.NoRole);
            }
        }

        if (!record.IsEnabled)
        {
            await NoteAsync(now, record.Id, AuthScheme.HostUser, "login.disabled", user.Name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.Disabled);
        }

        return await IssueAsync(record, AuthScheme.HostUser, address, agent, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Trades a refresh token for a fresh pair.
    /// </summary>
    public async Task<LoginResult> RefreshAsync(string refresh, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var rotated = await _refreshTokens.RotateAsync(refresh, now, ct).ConfigureAwait(false);
        if (rotated.Outcome != RefreshOutcome.Issued || rotated.Principal is null || rotated.Refresh is null)
        {
            await NoteAsync(now, rotated.Principal?.Id, AuthScheme.Bearer, Action("refresh", rotated.Outcome), null, null, ct).ConfigureAwait(false);

            return Failed(Reason(rotated.Outcome));
        }

        return new LoginResult(LoginOutcome.Ok, _issuer.Issue(rotated.Principal, now), rotated.Refresh, rotated.Principal);
    }

    /// <summary>
    /// Ends a session and everything issued in it.
    /// </summary>
    public async Task EndAsync(long sessionId, CancellationToken ct) =>
        await _refreshTokens.EndAsync(sessionId, _time.GetUtcNow(), ct).ConfigureAwait(false);

    /// <summary>
    /// Returns the role the groups of the host give an account, or null when they give none.
    /// </summary>
    public string? HostRole(LocalUser user)
    {
        if (_options.RootIsAdmin && user.Uid == 0)
        {
            return Roles.Admin;
        }

        foreach (var pair in _options.HostGroups)
        {
            if (LocalUsers.IsMemberOf(user, pair.Key))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the account a host user is registered as, or null when it is not registered.
    /// </summary>
    public AppUser? FindByHostUser(string userName) =>
        _users.Users.Where(user => user.HostUserName == userName).ToList().FirstOrDefault();

    private async Task SeenAsync(AppUser user, uint uid, DateTimeOffset now)
    {
        user.HostUid = uid;
        user.HostSeenUtc = now;
        await _users.UpdateAsync(user).ConfigureAwait(false);
    }

    private async Task SyncRoleAsync(AppUser user, string? granted, CancellationToken ct)
    {
        if (granted is not { Length: > 0 })
        {
            return;
        }

        var held = await _users.GetRolesAsync(user).ConfigureAwait(false);
        if (held.Contains(granted, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (held.Count > 0)
        {
            await _users.RemoveFromRolesAsync(user, held).ConfigureAwait(false);
        }

        await _users.AddToRoleAsync(user, granted).ConfigureAwait(false);
    }

    private async Task<LoginOutcome> FailAsync(AppUser user, DateTimeOffset now)
    {
        user.AccessFailedCount++;
        var locked = user.AccessFailedCount >= _options.FailedAttempts;
        if (locked)
        {
            user.AccessFailedCount = 0;
            user.LockoutEnd = now.Add(_options.LockDuration);
        }

        await _users.UpdateAsync(user).ConfigureAwait(false);

        return locked ? LoginOutcome.Locked : LoginOutcome.WrongPassword;
    }

    private async Task PassedAsync(AppUser user)
    {
        if (user.AccessFailedCount == 0 && user.LockoutEnd is null)
        {
            return;
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        await _users.UpdateAsync(user).ConfigureAwait(false);
    }

    private async Task<LoginResult> IssueAsync(
        AppUser user,
        AuthScheme scheme,
        string? address,
        string? agent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var session = await _refreshTokens
            .OpenAsync(user.Id, scheme, address, agent, now, ct)
            .ConfigureAwait(false);

        var scopes = (await _access.ScopesAsync(user).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        if (scheme == AuthScheme.Password)
        {
            scopes.Add(Scopes.ChangePassword);
        }

        var principal = new Principal(user.Id, user.Name, scheme, session.SessionId, scopes);
        await NoteAsync(now, user.Id, scheme, "login.ok", user.Name, address, ct).ConfigureAwait(false);

        return new LoginResult(LoginOutcome.Ok, _issuer.Issue(principal, now), session.Refresh, principal);
    }

    private async Task NoteAsync(
        DateTimeOffset now,
        long? principalId,
        AuthScheme scheme,
        string action,
        string? target,
        string? address,
        CancellationToken ct) =>
        await _audit.WriteAsync(now, principalId, scheme, action, target, null, address, ct).ConfigureAwait(false);

    private static IReadOnlySet<string> Only(string scope) =>
        new HashSet<string>(StringComparer.Ordinal) { scope };

    private static string Action(string prefix, object outcome) =>
        string.Concat(prefix, ".", outcome.ToString()!.ToLowerInvariant());

    private static LoginResult Failed(LoginOutcome outcome) => new(outcome, null, null, null);

    private static LoginOutcome Reason(RefreshOutcome outcome) => outcome switch
    {
        RefreshOutcome.Expired => LoginOutcome.RefreshExpired,
        RefreshOutcome.Replayed => LoginOutcome.RefreshReplayed,
        RefreshOutcome.SessionEnded => LoginOutcome.SessionEnded,
        _ => LoginOutcome.RefreshUnknown,
    };
}
