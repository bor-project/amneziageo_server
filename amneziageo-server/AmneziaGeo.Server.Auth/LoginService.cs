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
    private readonly IPrincipals _principals;

    private readonly IPasswords _passwords;

    private readonly IRefreshTokens _refreshTokens;

    private readonly ITokenIssuer _issuer;

    private readonly IAuditLog _audit;

    private readonly AuthOptions _options;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public LoginService(
        IPrincipals principals,
        IPasswords passwords,
        IRefreshTokens refreshTokens,
        ITokenIssuer issuer,
        IAuditLog audit,
        AuthOptions options,
        TimeProvider? time = null)
    {
        _principals = principals;
        _passwords = passwords;
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
        var record = await _principals.FindAsync(name, ct).ConfigureAwait(false);
        if (record is null)
        {
            await NoteAsync(now, null, AuthScheme.Password, "login.unknown", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.UnknownUser);
        }

        if (!record.IsEnabled)
        {
            await NoteAsync(now, record.Id, AuthScheme.Password, "login.disabled", name, address, ct).ConfigureAwait(false);

            return Failed(LoginOutcome.Disabled);
        }

        var checkup = await _passwords.CheckAsync(record.Id, password, now, ct).ConfigureAwait(false);
        if (checkup is not (PasswordCheck.Ok or PasswordCheck.MustChange))
        {
            await NoteAsync(now, record.Id, AuthScheme.Password, Action("login", checkup), name, address, ct).ConfigureAwait(false);

            return Failed(Reason(checkup));
        }

        if (checkup == PasswordCheck.MustChange)
        {
            await NoteAsync(now, record.Id, AuthScheme.Password, "login.mustchange", name, address, ct).ConfigureAwait(false);
            var limited = new Principal(record.Id, record.Name, AuthScheme.Password, 0, Only(Scopes.ChangePassword));

            return new LoginResult(LoginOutcome.Ok, _issuer.Issue(limited, now), null, limited, MustChangePassword: true);
        }

        return await IssueAsync(record, AuthScheme.Password, address, agent, now, ct).ConfigureAwait(false);
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
        var record = await _principals.FindByHostUserAsync(user.Name, ct).ConfigureAwait(false);

        if (record is null)
        {
            if (await _principals.FindAsync(user.Name, ct).ConfigureAwait(false) is not null)
            {
                return Failed(LoginOutcome.NameTaken);
            }

            if (_options.HostLogin == HostLogin.Strict && granted == Role.None)
            {
                await NoteAsync(now, null, AuthScheme.HostUser, "login.norole", user.Name, address, ct).ConfigureAwait(false);

                return Failed(LoginOutcome.NoRole);
            }

            record = await _principals
                .RegisterHostUserAsync(user.Name, user.Uid, user.Name, granted, ct)
                .ConfigureAwait(false);

            await NoteAsync(now, record.Id, AuthScheme.HostUser, "login.register", user.Name, address, ct).ConfigureAwait(false);
        }
        else
        {
            await _principals.SeenAsync(record.Id, user.Uid, now, ct).ConfigureAwait(false);
            record = await SyncRoleAsync(record, granted, ct).ConfigureAwait(false);

            if (_options.HostLogin == HostLogin.Strict && record.Role == Role.None)
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
    /// Returns the role the groups of the host give an account.
    /// </summary>
    public Role HostRole(LocalUser user)
    {
        if (_options.RootIsAdmin && user.Uid == 0)
        {
            return Role.Admin;
        }

        var role = Role.None;
        foreach (var pair in _options.HostGroups)
        {
            if (pair.Value > role && LocalUsers.IsMemberOf(user, pair.Key))
            {
                role = pair.Value;
            }
        }

        return role;
    }

    private async Task<PrincipalRecord> SyncRoleAsync(PrincipalRecord record, Role granted, CancellationToken ct)
    {
        if (granted == Role.None || granted == record.Role)
        {
            return record;
        }

        await _principals.SetRoleAsync(record.Id, granted, ct).ConfigureAwait(false);

        return record with { Role = granted };
    }

    private async Task<LoginResult> IssueAsync(
        PrincipalRecord record,
        AuthScheme scheme,
        string? address,
        string? agent,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var session = await _refreshTokens
            .OpenAsync(record.Id, scheme, address, agent, now, ct)
            .ConfigureAwait(false);

        var scopes = record.Scopes.ToHashSet(StringComparer.Ordinal);
        if (scheme == AuthScheme.Password)
        {
            scopes.Add(Scopes.ChangePassword);
        }

        var principal = new Principal(record.Id, record.Name, scheme, session.SessionId, scopes);
        await NoteAsync(now, record.Id, scheme, "login.ok", record.Name, address, ct).ConfigureAwait(false);

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

    private static LoginOutcome Reason(PasswordCheck checkup) => checkup switch
    {
        PasswordCheck.NotSet => LoginOutcome.NoPassword,
        PasswordCheck.Locked => LoginOutcome.Locked,
        _ => LoginOutcome.WrongPassword,
    };

    private static LoginOutcome Reason(RefreshOutcome outcome) => outcome switch
    {
        RefreshOutcome.Expired => LoginOutcome.RefreshExpired,
        RefreshOutcome.Replayed => LoginOutcome.RefreshReplayed,
        RefreshOutcome.SessionEnded => LoginOutcome.SessionEnded,
        _ => LoginOutcome.RefreshUnknown,
    };
}
