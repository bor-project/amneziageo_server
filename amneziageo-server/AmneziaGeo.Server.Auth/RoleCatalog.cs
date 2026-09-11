using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// What managing a role produced.
/// </summary>
public enum RoleOutcome
{
    Ok = 0,
    BadName = 1,
    NameTaken = 2,
    Unknown = 3,
    Builtin = 4,
    InUse = 5,
    UnknownScope = 6,
    Failed = 7,
    HasTokens = 8,
}

/// <summary>
/// The role a command produced, and why it was refused.
/// </summary>
public sealed record RoleResult(RoleOutcome Outcome, string Message, AppRole? Record)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Outcome == RoleOutcome.Ok;

    /// <summary>
    /// Returns the role a command produced.
    /// </summary>
    public static RoleResult Done(AppRole role) => new(RoleOutcome.Ok, string.Empty, role);

    /// <summary>
    /// Returns a refusal with the reason behind it.
    /// </summary>
    public static RoleResult No(RoleOutcome outcome, string message) => new(outcome, message, null);
}

/// <summary>
/// A role with the rights it carries and the number of accounts it is given to.
/// </summary>
public sealed record RoleView(AppRole Record, IReadOnlySet<string> Scopes, int Users);

/// <summary>
/// Adds, changes and removes roles by one set of rules.
/// </summary>
public sealed class RoleCatalog
{
    private readonly RoleManager<AppRole> _roles;

    private readonly UserManager<AppUser> _users;

    private readonly AccessResolver _access;

    private readonly IApiTokens _tokens;

    private readonly TimeProvider _time;

    /// <summary>
    /// ctor
    /// </summary>
    public RoleCatalog(
        RoleManager<AppRole> roles,
        UserManager<AppUser> users,
        AccessResolver access,
        IApiTokens tokens,
        TimeProvider? time = null)
    {
        _roles = roles;
        _users = users;
        _access = access;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns every role with its rights and the number of accounts it is given to.
    /// </summary>
    public async Task<IReadOnlyList<RoleView>> ListAsync(CancellationToken ct)
    {
        var found = _roles.Roles.OrderBy(role => role.Name).ToList();
        var views = new List<RoleView>(found.Count);
        foreach (var role in found)
        {
            views.Add(await ViewAsync(role).ConfigureAwait(false));
        }

        return views;
    }

    /// <summary>
    /// Returns a role with its rights, or null when there is none.
    /// </summary>
    public async Task<RoleView?> FindAsync(string name, CancellationToken ct)
    {
        var role = await _roles.FindByNameAsync(name).ConfigureAwait(false);

        return role is null ? null : await ViewAsync(role).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a role with the rights it carries.
    /// </summary>
    public async Task<RoleResult> AddAsync(string name, string? title, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        var wanted = (name ?? string.Empty).Trim();
        if (AccountRules.CheckName(wanted) is { } shape)
        {
            return RoleResult.No(RoleOutcome.BadName, shape);
        }

        if (await _roles.FindByNameAsync(wanted).ConfigureAwait(false) is not null)
        {
            return RoleResult.No(RoleOutcome.NameTaken, $"the panel already carries a role called '{wanted}'");
        }

        if (Unknown(scopes) is { } complaint)
        {
            return complaint;
        }

        var role = new AppRole(wanted)
        {
            Title = string.IsNullOrWhiteSpace(title) ? wanted : title.Trim(),
            CreatedUtc = _time.GetUtcNow(),
        };

        var added = await _roles.CreateAsync(role).ConfigureAwait(false);
        if (!added.Succeeded)
        {
            return Failed(added);
        }

        await ApplyAsync(role, scopes).ConfigureAwait(false);

        return RoleResult.Done(role);
    }

    /// <summary>
    /// Replaces the title of a role, the rights it carries, or both.
    /// </summary>
    public async Task<RoleResult> ChangeAsync(string name, string? title, IReadOnlyCollection<string>? scopes, CancellationToken ct)
    {
        var role = await _roles.FindByNameAsync(name).ConfigureAwait(false);
        if (role is null)
        {
            return Missing(name);
        }

        if (scopes is not null)
        {
            if (role.IsBuiltin)
            {
                return RoleResult.No(RoleOutcome.Builtin, $"the rights of the role '{role.Name}' are the ones the server keeps");
            }

            if (Unknown(scopes) is { } complaint)
            {
                return complaint;
            }

            await ApplyAsync(role, scopes).ConfigureAwait(false);
        }

        if (title is { Length: > 0 })
        {
            role.Title = title.Trim();
            var saved = await _roles.UpdateAsync(role).ConfigureAwait(false);
            if (!saved.Succeeded)
            {
                return Failed(saved);
            }
        }

        return RoleResult.Done(role);
    }

    /// <summary>
    /// Removes a role no account and no token is given.
    /// </summary>
    public async Task<RoleResult> RemoveAsync(string name, CancellationToken ct)
    {
        var role = await _roles.FindByNameAsync(name).ConfigureAwait(false);
        if (role is null)
        {
            return Missing(name);
        }

        if (role.IsBuiltin)
        {
            return RoleResult.No(RoleOutcome.Builtin, $"the role '{role.Name}' is the one the server keeps");
        }

        var held = await _users.GetUsersInRoleAsync(role.Name!).ConfigureAwait(false);
        if (held.Count > 0)
        {
            return RoleResult.No(RoleOutcome.InUse, $"the role '{role.Name}' is given to {held.Count} accounts");
        }

        var tokens = await _tokens.CountAsync(role.Name!, ct).ConfigureAwait(false);
        if (tokens > 0)
        {
            return RoleResult.No(RoleOutcome.HasTokens, $"the role '{role.Name}' carries {tokens} tokens");
        }

        var removed = await _roles.DeleteAsync(role).ConfigureAwait(false);

        return removed.Succeeded ? RoleResult.Done(role) : Failed(removed);
    }

    /// <summary>
    /// Returns the roles that carry a right.
    /// </summary>
    public async Task<IReadOnlyList<string>> CarryingAsync(string scope, CancellationToken ct)
    {
        var carrying = new List<string>();
        foreach (var role in _roles.Roles.ToList())
        {
            if ((await _access.ScopesAsync(role).ConfigureAwait(false)).Contains(scope))
            {
                carrying.Add(role.Name!);
            }
        }

        return carrying;
    }

    private async Task<RoleView> ViewAsync(AppRole role)
    {
        var scopes = await _access.ScopesAsync(role).ConfigureAwait(false);
        var held = await _users.GetUsersInRoleAsync(role.Name!).ConfigureAwait(false);

        return new RoleView(role, scopes, held.Count);
    }

    private async Task ApplyAsync(AppRole role, IReadOnlyCollection<string> scopes)
    {
        var held = await _roles.GetClaimsAsync(role).ConfigureAwait(false);
        var wanted = scopes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        foreach (var claim in held.Where(claim => claim.Type == Scopes.ClaimType))
        {
            if (!wanted.Remove(claim.Value))
            {
                await _roles.RemoveClaimAsync(role, claim).ConfigureAwait(false);
            }
        }

        foreach (var scope in wanted)
        {
            await _roles.AddClaimAsync(role, new Claim(Scopes.ClaimType, scope)).ConfigureAwait(false);
        }
    }

    private static RoleResult? Unknown(IReadOnlyCollection<string> scopes)
    {
        var unknown = scopes.Where(scope => !Scopes.Known(scope)).ToArray();

        return unknown.Length == 0
            ? null
            : RoleResult.No(RoleOutcome.UnknownScope, $"there are no rights called {string.Join(" ", unknown)}");
    }

    private static RoleResult Failed(IdentityResult result) =>
        RoleResult.No(RoleOutcome.Failed, string.Join("; ", result.Errors.Select(error => error.Description)));

    private static RoleResult Missing(string name) =>
        RoleResult.No(RoleOutcome.Unknown, $"there is no role called '{name}'");
}
