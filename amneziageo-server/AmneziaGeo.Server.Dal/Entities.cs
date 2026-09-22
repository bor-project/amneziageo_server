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
/// A long lived token that acts by a role, kept as a hash.
/// </summary>
public sealed class ApiTokenEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? ExpiresUtc { get; set; }

    public DateTimeOffset? LastUsedUtc { get; set; }

    public string? LastAddress { get; set; }
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
/// The obfuscation columns a table carries.
/// </summary>
public interface IObfuscated
{
    /// <summary>
    /// How many junk packets go before a handshake.
    /// </summary>
    int Jc { get; set; }

    /// <summary>
    /// The shortest junk packet, in bytes.
    /// </summary>
    int Jmin { get; set; }

    /// <summary>
    /// The longest junk packet, in bytes.
    /// </summary>
    int Jmax { get; set; }

    /// <summary>
    /// Junk prepended to a handshake initiation, in bytes.
    /// </summary>
    int S1 { get; set; }

    /// <summary>
    /// Junk prepended to a handshake response, in bytes.
    /// </summary>
    int S2 { get; set; }

    /// <summary>
    /// Junk prepended to a cookie reply, in bytes.
    /// </summary>
    int S3 { get; set; }

    /// <summary>
    /// Junk prepended to a transport packet, in bytes.
    /// </summary>
    int S4 { get; set; }

    /// <summary>
    /// The type written into a handshake initiation.
    /// </summary>
    string H1 { get; set; }

    /// <summary>
    /// The type written into a handshake response.
    /// </summary>
    string H2 { get; set; }

    /// <summary>
    /// The type written into a cookie reply.
    /// </summary>
    string H3 { get; set; }

    /// <summary>
    /// The type written into a transport packet.
    /// </summary>
    string H4 { get; set; }

    /// <summary>
    /// The first special junk packet.
    /// </summary>
    string? I1 { get; set; }

    /// <summary>
    /// The second special junk packet.
    /// </summary>
    string? I2 { get; set; }

    /// <summary>
    /// The third special junk packet.
    /// </summary>
    string? I3 { get; set; }

    /// <summary>
    /// The fourth special junk packet.
    /// </summary>
    string? I4 { get; set; }

    /// <summary>
    /// The fifth special junk packet.
    /// </summary>
    string? I5 { get; set; }

    /// <summary>
    /// The key the packet header is hidden with.
    /// </summary>
    string HeaderProtectionKey { get; set; }

    /// <summary>
    /// Padding added to the content of a transport packet.
    /// </summary>
    string ContentPaddingAddition { get; set; }

    /// <summary>
    /// When a session is renewed.
    /// </summary>
    string RekeyAfterTime { get; set; }

    /// <summary>
    /// How long a handshake attempt waits before it repeats.
    /// </summary>
    string RekeyTimeout { get; set; }

    /// <summary>
    /// When a session stops being accepted.
    /// </summary>
    string RejectAfterTime { get; set; }

    /// <summary>
    /// How long a quiet path is held open.
    /// </summary>
    string KeepaliveTimeout { get; set; }

    /// <summary>
    /// How many handshakes are attempted before the peer is given up on.
    /// </summary>
    string MaxHandshakeAttempts { get; set; }

    /// <summary>
    /// Whether random bytes are appended to packets.
    /// </summary>
    bool RandomTrailers { get; set; }

    /// <summary>
    /// Whether cookie replies are turned off.
    /// </summary>
    bool DisableCookies { get; set; }
}

/// <summary>
/// The settings of one server endpoint the panel holds.
/// </summary>
public sealed class ConfigEntity : IObfuscated
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

    public int OfflineAfter { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool Nat { get; set; } = true;

    public bool Opened { get; set; }

    public string Blocked { get; set; } = string.Empty;

    public int Inbound { get; set; }

    public bool WebSocket { get; set; }

    public int ServicesPort { get; set; }

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

    public string H1 { get; set; } = string.Empty;

    public string H2 { get; set; } = string.Empty;

    public string H3 { get; set; } = string.Empty;

    public string H4 { get; set; } = string.Empty;

    public string? I1 { get; set; }

    public string? I2 { get; set; }

    public string? I3 { get; set; }

    public string? I4 { get; set; }

    public string? I5 { get; set; }

    public string HeaderProtectionKey { get; set; } = string.Empty;

    public string ContentPaddingAddition { get; set; } = string.Empty;

    public string RekeyAfterTime { get; set; } = string.Empty;

    public string RekeyTimeout { get; set; } = string.Empty;

    public string RejectAfterTime { get; set; } = string.Empty;

    public string KeepaliveTimeout { get; set; } = string.Empty;

    public string MaxHandshakeAttempts { get; set; } = string.Empty;

    public bool RandomTrailers { get; set; }

    public bool DisableCookies { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// One geo database the panel downloads and matches rules against.
/// </summary>
public sealed class GeoSourceEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public int Position { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? UpdatedUtc { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public int EntryCount { get; set; }

    public long Size { get; set; }

    public string ETag { get; set; } = string.Empty;

    public string LastModified { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}

/// <summary>
/// One way out of the host the panel holds.
/// </summary>
public sealed class OutboundEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public int Position { get; set; }

    public bool IsEnabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Proxy { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string PeerKey { get; set; } = string.Empty;

    public string PresharedKey { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Dns { get; set; } = string.Empty;

    public int Mtu { get; set; }

    public int Keepalive { get; set; }

    public string Probe { get; set; } = string.Empty;

    public int ProbeEvery { get; set; }

    public bool ClosePrivate { get; set; }

    public long Mark { get; set; }

    public int Table { get; set; }

    public int Jc { get; set; }

    public int Jmin { get; set; }

    public int Jmax { get; set; }

    public int S1 { get; set; }

    public int S2 { get; set; }

    public int S3 { get; set; }

    public int S4 { get; set; }

    public string H1 { get; set; } = string.Empty;

    public string H2 { get; set; } = string.Empty;

    public string H3 { get; set; } = string.Empty;

    public string H4 { get; set; } = string.Empty;

    public string? I1 { get; set; }

    public string? I2 { get; set; }

    public string? I3 { get; set; }

    public string? I4 { get; set; }

    public string? I5 { get; set; }

    public string HeaderProtectionKey { get; set; } = string.Empty;

    public string ContentPaddingAddition { get; set; } = string.Empty;

    public string RekeyAfterTime { get; set; } = string.Empty;

    public string RekeyTimeout { get; set; } = string.Empty;

    public string RejectAfterTime { get; set; } = string.Empty;

    public string KeepaliveTimeout { get; set; } = string.Empty;

    public string MaxHandshakeAttempts { get; set; } = string.Empty;

    public bool RandomTrailers { get; set; }

    public bool DisableCookies { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// One routing rule as the database holds it.
/// </summary>
public sealed class RouteRuleEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Position { get; set; }

    public bool IsEnabled { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Outbound { get; set; } = string.Empty;

    public bool HoldsWhenDown { get; set; }

    public string Targets { get; set; } = string.Empty;

    public string Sources { get; set; } = string.Empty;

    public string Clients { get; set; } = string.Empty;

    public string Inbounds { get; set; } = string.Empty;

    public string Ports { get; set; } = string.Empty;

    public string SourcePorts { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// The basic routing lists as the database holds them.
/// </summary>
public sealed class RouteBasicEntity
{
    public long Id { get; set; }

    public string Direct { get; set; } = string.Empty;

    public string Block { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// The settings of the panel as the database holds them.
/// </summary>
public sealed class PanelEntity
{
    public long Id { get; set; }

    public string Listen { get; set; } = string.Empty;

    public string Domains { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool Opened { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Certificate { get; set; } = string.Empty;

    public string CertificateKey { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public bool Prereleases { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// The resolver the panel runs for the clients of the tunnels.
/// </summary>
public sealed class DnsSettingsEntity
{
    public long Id { get; set; }

    public bool IsEnabled { get; set; }

    public int Port { get; set; }

    public string Upstreams { get; set; } = string.Empty;

    public string Outbound { get; set; } = string.Empty;

    public string Listen { get; set; } = string.Empty;

    public int NameMinutes { get; set; }

    public int CacheSize { get; set; }

    public int MinTtl { get; set; }

    public int MaxTtl { get; set; }

    public bool Intercept { get; set; }

    public bool BlockDot { get; set; }

    public bool BlockDoh { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// One address a rule stands on as the database holds it.
/// </summary>
public sealed class DnsStandingEntity
{
    public long Id { get; set; }

    public long RuleId { get; set; }

    public string Address { get; set; } = string.Empty;

    public DateTimeOffset SeenUtc { get; set; }
}

/// <summary>
/// One balancer as the database holds it.
/// </summary>
public sealed class BalancerEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Position { get; set; }

    public bool IsEnabled { get; set; }

    public string Strategy { get; set; } = string.Empty;

    public string Members { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// One client of an endpoint as the database holds it.
/// </summary>
public sealed class ClientEntity
{
    public long Id { get; set; }

    public long ConfigId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string PresharedKey { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public string Note { get; set; } = string.Empty;

    public long? TemplateId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public long? ParentId { get; set; }

    public bool MultiDevice { get; set; }

    public long DailyLimit { get; set; }

    public int Inbound { get; set; }

    public int Routing { get; set; }

    public string Routes { get; set; } = string.Empty;

    public string Forwards { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// The traffic of one client over one day as the database holds it.
/// </summary>
public sealed class TrafficEntity
{
    public long Id { get; set; }

    public long ClientId { get; set; }

    public DateOnly Day { get; set; }

    public long Rx { get; set; }

    public long Tx { get; set; }

    public long SeenRx { get; set; }

    public long SeenTx { get; set; }
}

/// <summary>
/// The settings of the subscriptions as the database holds them.
/// </summary>
public sealed class SubscriptionEntity
{
    public long Id { get; set; }

    public bool IsEnabled { get; set; }

    public bool Separate { get; set; }

    public string Listen { get; set; } = string.Empty;

    public string Domains { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool Opened { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Certificate { get; set; } = string.Empty;

    public string CertificateKey { get; set; } = string.Empty;

    public int UpdateHours { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// One client template as the database holds it.
/// </summary>
public sealed class TemplateEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Entries { get; set; } = string.Empty;

    public string AllowedIps { get; set; } = string.Empty;

    public string Missed { get; set; } = string.Empty;

    public string Dns { get; set; } = string.Empty;

    public int? Mtu { get; set; }

    public int? Keepalive { get; set; }

    public bool LocksRouting { get; set; }

    public DateTimeOffset? RefreshedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
