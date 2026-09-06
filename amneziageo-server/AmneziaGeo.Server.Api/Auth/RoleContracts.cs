using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Auth;

/// <summary>
/// A role as the interface reads it.
/// </summary>
public sealed record RoleResponse(string Name, string Title, bool Builtin, string[] Scopes, int Users);

/// <summary>
/// The roles of the panel with the rights the server knows.
/// </summary>
public sealed record RoleListResponse(RoleResponse[] Roles, string[] Scopes);

/// <summary>
/// A new role as the interface sends it.
/// </summary>
public sealed record RoleCreateRequest(string? Name, string? Title, string[]? Scopes);

/// <summary>
/// The parts of a role the interface changes.
/// </summary>
public sealed record RolePatchRequest(string? Title, string[]? Scopes);

/// <summary>
/// Builds the role answers of the management routes.
/// </summary>
public static class RoleAnswers
{
    /// <summary>
    /// Describes a role as the interface reads it.
    /// </summary>
    public static RoleResponse Role(RoleView view) => new(
        view.Record.Name ?? string.Empty,
        view.Record.Title,
        view.Record.IsBuiltin,
        [.. view.Scopes.Order(StringComparer.Ordinal)],
        view.Users);
}
