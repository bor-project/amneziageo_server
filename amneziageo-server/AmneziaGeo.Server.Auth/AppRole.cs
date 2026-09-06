using Microsoft.AspNetCore.Identity;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// A role of the panel, carrying its rights as claims.
/// </summary>
public sealed class AppRole : IdentityRole<long>
{
    /// <summary>
    /// ctor
    /// </summary>
    public AppRole()
    {
    }

    /// <summary>
    /// ctor
    /// </summary>
    public AppRole(string name)
        : base(name)
    {
    }

    /// <summary>
    /// The name the panel shows.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Whether the server keeps the role as it is.
    /// </summary>
    public bool IsBuiltin { get; set; }

    /// <summary>
    /// When the role was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
