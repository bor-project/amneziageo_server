using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Reads the rights an account holds out of the claims of its roles.
/// </summary>
public sealed class AccessResolver
{
    private readonly UserManager<AppUser> _users;

    private readonly RoleManager<AppRole> _roles;

    /// <summary>
    /// ctor
    /// </summary>
    public AccessResolver(UserManager<AppUser> users, RoleManager<AppRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    /// <summary>
    /// Returns the rights the roles of an account carry, together with the ones given to it directly.
    /// </summary>
    public async Task<IReadOnlySet<string>> ScopesAsync(AppUser user)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in await _users.GetRolesAsync(user).ConfigureAwait(false))
        {
            var role = await _roles.FindByNameAsync(name).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }

            held.UnionWith(Held(await _roles.GetClaimsAsync(role).ConfigureAwait(false)));
        }

        held.UnionWith(Held(await _users.GetClaimsAsync(user).ConfigureAwait(false)));

        return held;
    }

    /// <summary>
    /// Returns the role an account is given, or an empty string when it has none.
    /// </summary>
    public async Task<string> RoleAsync(AppUser user) =>
        (await _users.GetRolesAsync(user).ConfigureAwait(false)).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Returns the rights a role carries.
    /// </summary>
    public async Task<IReadOnlySet<string>> ScopesAsync(AppRole role) =>
        Held(await _roles.GetClaimsAsync(role).ConfigureAwait(false));

    /// <summary>
    /// Tells whether an account holds a right.
    /// </summary>
    public async Task<bool> HoldsAsync(AppUser user, string scope) =>
        (await ScopesAsync(user).ConfigureAwait(false)).Contains(scope);

    private static HashSet<string> Held(IEnumerable<Claim> claims) =>
        claims.Where(claim => claim.Type == Scopes.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
}
