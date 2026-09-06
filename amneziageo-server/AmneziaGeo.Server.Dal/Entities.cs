using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// A sign in that stays open until it is ended or reaches its ceiling.
/// </summary>
public sealed class SessionEntity
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public AppUser? User { get; set; }

    public AuthScheme Scheme { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset AbsoluteEndUtc { get; set; }

    public DateTimeOffset? EndedUtc { get; set; }

    public string? Address { get; set; }

    public string? Agent { get; set; }
}

/// <summary>
/// A refresh token of a session, kept as a hash.
/// </summary>
public sealed class RefreshTokenEntity
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    public SessionEntity? Session { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset IssuedUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }

    public DateTimeOffset? GraceEndUtc { get; set; }

    public DateTimeOffset? UsedUtc { get; set; }

    public long? ReplacedBy { get; set; }
}

/// <summary>
/// One line of the audit trail.
/// </summary>
public sealed class AuditEntity
{
    public long Id { get; set; }

    public DateTimeOffset AtUtc { get; set; }

    public long? UserId { get; set; }

    public AuthScheme Scheme { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Target { get; set; }

    public string? Detail { get; set; }

    public string? Address { get; set; }
}
