using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What managing an account produced.
/// </summary>
public enum AccountOutcome
{
    Ok = 0,
    BadName = 1,
    NameTaken = 2,
    HostName = 3,
    Unknown = 4,
    LastAdmin = 5,
    Weak = 6,
    UnknownRole = 7,
    Self = 8,
    Failed = 9,
    BadKey = 10,
}

/// <summary>
/// The account a command produced, and why it was refused.
/// </summary>
public sealed record AccountResult(AccountOutcome Outcome, string Message, AppUser? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == AccountOutcome.Ok;

    /// <summary>
    /// Returns the account a command produced.
    /// </summary>
    public static AccountResult Done(AppUser user) => new(AccountOutcome.Ok, string.Empty, user);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static AccountResult No(AccountOutcome outcome, string message) => new(outcome, message, null);
}

/// <summary>
/// An account with the facts the panel shows beside it.
/// </summary>
public sealed record AccountView(AppUser Record, string Role, IReadOnlySet<string> Scopes, bool HasPassword);

/// <summary>
/// Adds, changes and removes accounts of the panel by one set of rules.
/// </summary>
public sealed class AccountManager
{
    private readonly UserManager<AppUser> _users;

    private readonly RoleManager<AppRole> _roles;

    private readonly AccessResolver _access;

    private readonly AuthOptions _options;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public AccountManager(
        UserManager<AppUser> users,
        RoleManager<AppRole> roles,
        AccessResolver access,
        AuthOptions options,
        TimeProvider? time = null)
    {
        _users = users;
        _roles = roles;
        _access = access;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every account with the facts the panel shows beside it.
    /// </summary>
    public async Task<IReadOnlyList<AccountView>> ListAsync(CancellationToken ct)
    {
        var found = _users.Users.OrderBy(user => user.UserName).ToList();
        var views = new List<AccountView>(found.Count);
        foreach (var user in found)
        {
            views.Add(await ViewAsync(user).ConfigureAwait(false));
        }

        return views;
    }

    /// <summary>
    /// Returns an account with the facts the panel shows beside it, or null when there is none.
    /// </summary>
    public async Task<AccountView?> FindAsync(string name, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);

        return user is null ? null : await ViewAsync(user).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns why a name cannot be taken, or null when it can.
    /// </summary>
    public async Task<AccountResult?> WhyNotAsync(string name, CancellationToken ct) =>
        await WhyNotAsync(name, false, ct).ConfigureAwait(false);

    /// <summary>
    /// Returns why a name cannot be taken, leaving the users of the host to a privileged account.
    /// </summary>
    public async Task<AccountResult?> WhyNotAsync(string name, bool host, CancellationToken ct)
    {
        if (AccountRules.CheckName(name) is { } shape)
        {
            return AccountResult.No(AccountOutcome.BadName, shape);
        }

        if (!host && LocalUsers.Find(name) is not null)
        {
            return AccountResult.No(AccountOutcome.HostName, $"the host carries a user called '{name}', that name is left to it");
        }

        if (await _users.FindByNameAsync(name).ConfigureAwait(false) is not null)
        {
            return AccountResult.No(AccountOutcome.NameTaken, $"the panel already carries a user called '{name}'");
        }

        return null;
    }

    /// <summary>
    /// Tells whether an account that manages access is enabled.
    /// </summary>
    public async Task<bool> HasAdminAsync(CancellationToken ct) =>
        await AnotherKeeperAsync(0, ct).ConfigureAwait(false);

    /// <summary>
    /// Adds an account of the panel with its first password.
    /// </summary>
    public async Task<AccountResult> AddAsync(
        string name,
        string? displayName,
        string? role,
        string password,
        bool mustChange,
        CancellationToken ct) =>
        await AddAsync(name, displayName, role, password, mustChange, false, null, ct).ConfigureAwait(false);

    /// <summary>
    /// Adds an account of the panel, standing for a user of the host when it is a privileged one.
    /// </summary>
    public async Task<AccountResult> AddAsync(
        string name,
        string? displayName,
        string? role,
        string password,
        bool mustChange,
        bool host,
        string? key,
        CancellationToken ct)
    {
        if (await WhyNotAsync(name, host, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        if (host && key is { Length: > 0 } && !HostUserPlan.IsKey(key))
        {
            return AccountResult.No(AccountOutcome.BadKey, "the public key is not one the host takes");
        }

        if (AccountRules.CheckPassword(password, _options.MinimumPasswordLength) is { } complaint)
        {
            return AccountResult.No(AccountOutcome.Weak, complaint);
        }

        if (role is { Length: > 0 } && await _roles.FindByNameAsync(role).ConfigureAwait(false) is null)
        {
            return UnknownRole(role);
        }

        var user = new AppUser
        {
            UserName = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
            Kind = host ? PrincipalKind.Host : PrincipalKind.Local,
            IsEnabled = true,
            MustChangePassword = mustChange,
            CreatedUtc = _time.GetUtcNow(),
            PasswordChangedUtc = _time.GetUtcNow(),
            HostUserName = host ? name : null,
            HostUid = host ? (long?)LocalUsers.Find(name)?.Uid : null,
            HostKey = host && key is { Length: > 0 } ? key.Trim() : null,
        };

        var created = await _users.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return Failed(created);
        }

        if (role is { Length: > 0 })
        {
            var given = await _users.AddToRoleAsync(user, role).ConfigureAwait(false);
            if (!given.Succeeded)
            {
                return Failed(given);
            }
        }

        return AccountResult.Done(user);
    }

    /// <summary>
    /// Registers a host user as an account of the panel.
    /// </summary>
    public async Task<AccountResult> RegisterHostUserAsync(string name, uint uid, string? role, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName = name,
            DisplayName = name,
            Kind = PrincipalKind.Host,
            IsEnabled = true,
            CreatedUtc = _time.GetUtcNow(),
            HostUserName = name,
            HostUid = uid,
            HostSeenUtc = _time.GetUtcNow(),
        };

        var created = await _users.CreateAsync(user).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return Failed(created);
        }

        if (role is { Length: > 0 } && await _roles.FindByNameAsync(role).ConfigureAwait(false) is not null)
        {
            await _users.AddToRoleAsync(user, role).ConfigureAwait(false);
        }

        return AccountResult.Done(user);
    }

    /// <summary>
    /// Gives an account one role, replacing the ones it holds.
    /// </summary>
    public async Task<AccountResult> SetRoleAsync(string name, string role, long actorId, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(name);
        }

        if (role is { Length: > 0 } && await _roles.FindByNameAsync(role).ConfigureAwait(false) is null)
        {
            return UnknownRole(role);
        }

        var held = await _users.GetRolesAsync(user).ConfigureAwait(false);
        if (held.Count == 1 && string.Equals(held[0], role, StringComparison.OrdinalIgnoreCase))
        {
            return AccountResult.Done(user);
        }

        if (await KeepsAccessAsync(user).ConfigureAwait(false) && !await Keeps(role, ct).ConfigureAwait(false))
        {
            if (user.Id == actorId)
            {
                return AccountResult.No(AccountOutcome.Self, "an administrator does not take their own rights away");
            }

            if (!await AnotherKeeperAsync(user.Id, ct).ConfigureAwait(false))
            {
                return LastAdmin();
            }
        }

        if (held.Count > 0)
        {
            var dropped = await _users.RemoveFromRolesAsync(user, held).ConfigureAwait(false);
            if (!dropped.Succeeded)
            {
                return Failed(dropped);
            }
        }

        if (role is { Length: > 0 })
        {
            var given = await _users.AddToRoleAsync(user, role).ConfigureAwait(false);
            if (!given.Succeeded)
            {
                return Failed(given);
            }
        }

        return AccountResult.Done(user);
    }

    /// <summary>
    /// Writes the public key a privileged account signs in to the host with.
    /// </summary>
    public async Task<AccountResult> SetHostKeyAsync(string name, string key, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(name);
        }

        if (user.HostUserName is not { Length: > 0 })
        {
            return AccountResult.No(AccountOutcome.BadKey, $"'{name}' is no user of the host");
        }

        if (!HostUserPlan.IsKey(key))
        {
            return AccountResult.No(AccountOutcome.BadKey, "the public key is not one the host takes");
        }

        user.HostKey = key.Trim();

        var saved = await _users.UpdateAsync(user).ConfigureAwait(false);

        return saved.Succeeded ? AccountResult.Done(user) : Failed(saved);
    }

    /// <summary>
    /// Turns an account on or off.
    /// </summary>
    public async Task<AccountResult> SetEnabledAsync(string name, bool enabled, long actorId, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(name);
        }

        if (!enabled)
        {
            if (user.Id == actorId)
            {
                return AccountResult.No(AccountOutcome.Self, "an account does not switch itself off");
            }

            if (await KeepsAccessAsync(user).ConfigureAwait(false) && !await AnotherKeeperAsync(user.Id, ct).ConfigureAwait(false))
            {
                return LastAdmin();
            }
        }

        user.IsEnabled = enabled;
        user.DisabledUtc = enabled ? null : _time.GetUtcNow();

        var saved = await _users.UpdateAsync(user).ConfigureAwait(false);

        return saved.Succeeded ? AccountResult.Done(user) : Failed(saved);
    }

    /// <summary>
    /// Replaces the password of an account and clears its lockout.
    /// </summary>
    public async Task<AccountResult> SetPasswordAsync(string name, string password, bool mustChange, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(name);
        }

        if (AccountRules.CheckPassword(password, _options.MinimumPasswordLength) is { } complaint)
        {
            return AccountResult.No(AccountOutcome.Weak, complaint);
        }

        if (await _users.HasPasswordAsync(user).ConfigureAwait(false))
        {
            var dropped = await _users.RemovePasswordAsync(user).ConfigureAwait(false);
            if (!dropped.Succeeded)
            {
                return Failed(dropped);
            }
        }

        var added = await _users.AddPasswordAsync(user, password).ConfigureAwait(false);
        if (!added.Succeeded)
        {
            return Failed(added);
        }

        user.MustChangePassword = mustChange;
        user.PasswordChangedUtc = _time.GetUtcNow();
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;

        var saved = await _users.UpdateAsync(user).ConfigureAwait(false);

        return saved.Succeeded ? AccountResult.Done(user) : Failed(saved);
    }

    /// <summary>
    /// Replaces the password an account signs in with, checking the one it carries now.
    /// </summary>
    public async Task<AccountResult> ChangePasswordAsync(long id, string current, string next, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(id.ToString());
        }

        if (AccountRules.CheckPassword(next, _options.MinimumPasswordLength) is { } complaint)
        {
            return AccountResult.No(AccountOutcome.Weak, complaint);
        }

        var changed = await _users.ChangePasswordAsync(user, current, next).ConfigureAwait(false);
        if (!changed.Succeeded)
        {
            return Failed(changed);
        }

        user.MustChangePassword = false;
        user.PasswordChangedUtc = _time.GetUtcNow();

        var saved = await _users.UpdateAsync(user).ConfigureAwait(false);

        return saved.Succeeded ? AccountResult.Done(user) : Failed(saved);
    }

    /// <summary>
    /// Removes an account with its sessions and tokens.
    /// </summary>
    public async Task<AccountResult> RemoveAsync(string name, long actorId, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            return Missing(name);
        }

        if (user.Id == actorId)
        {
            return AccountResult.No(AccountOutcome.Self, "an account does not remove itself");
        }

        if (await KeepsAccessAsync(user).ConfigureAwait(false) && !await AnotherKeeperAsync(user.Id, ct).ConfigureAwait(false))
        {
            return LastAdmin();
        }

        var removed = await _users.DeleteAsync(user).ConfigureAwait(false);

        return removed.Succeeded ? AccountResult.Done(user) : Failed(removed);
    }

    /// <summary>
    /// Describes an account the way the panel shows it.
    /// </summary>
    public async Task<AccountView> ViewAsync(AppUser user) => new(
        user,
        await _access.RoleAsync(user).ConfigureAwait(false),
        await _access.ScopesAsync(user).ConfigureAwait(false),
        await _users.HasPasswordAsync(user).ConfigureAwait(false));

    private async Task<bool> KeepsAccessAsync(AppUser user) =>
        (await _access.ScopesAsync(user).ConfigureAwait(false)).Contains(Scopes.ManageAccess);

    private async Task<bool> Keeps(string? role, CancellationToken ct)
    {
        if (role is not { Length: > 0 })
        {
            return false;
        }

        var found = await _roles.FindByNameAsync(role).ConfigureAwait(false);

        return found is not null && (await _access.ScopesAsync(found).ConfigureAwait(false)).Contains(Scopes.ManageAccess);
    }

    private async Task<bool> AnotherKeeperAsync(long id, CancellationToken ct)
    {
        foreach (var role in _roles.Roles.ToList())
        {
            if (!(await _access.ScopesAsync(role).ConfigureAwait(false)).Contains(Scopes.ManageAccess))
            {
                continue;
            }

            var held = await _users.GetUsersInRoleAsync(role.Name!).ConfigureAwait(false);
            if (held.Any(user => user.Id != id && user.IsEnabled))
            {
                return true;
            }
        }

        return false;
    }

    private static AccountResult Failed(IdentityResult result) =>
        AccountResult.No(AccountOutcome.Failed, string.Join("; ", result.Errors.Select(error => error.Description)));

    private static AccountResult Missing(string name) =>
        AccountResult.No(AccountOutcome.Unknown, $"there is no user called '{name}'");

    private static AccountResult UnknownRole(string role) =>
        AccountResult.No(AccountOutcome.UnknownRole, $"there is no role called '{role}'");

    private static AccountResult LastAdmin() =>
        AccountResult.No(AccountOutcome.LastAdmin, "this is the last administrator");
}
