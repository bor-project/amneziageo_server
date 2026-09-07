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

/// <summary>
/// The settings of one server endpoint the panel holds.
/// </summary>
public sealed class ConfigEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int ListenPort { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Dns { get; set; } = string.Empty;

    public string AllowedIps { get; set; } = string.Empty;

    public int Mtu { get; set; }

    public int Keepalive { get; set; }

    public string PrivateKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string PresharedKey { get; set; } = string.Empty;

    public int Jc { get; set; }

    public int Jmin { get; set; }

    public int Jmax { get; set; }

    public int S1 { get; set; }

    public int S2 { get; set; }

    public int S3 { get; set; }

    public int S4 { get; set; }

    public long H1 { get; set; }

    public long H2 { get; set; }

    public long H3 { get; set; }

    public long H4 { get; set; }

    public string? I1 { get; set; }

    public string? I2 { get; set; }

    public string? I3 { get; set; }

    public string? I4 { get; set; }

    public string? I5 { get; set; }

    public bool RandomTrailers { get; set; }

    public bool DisableCookies { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
