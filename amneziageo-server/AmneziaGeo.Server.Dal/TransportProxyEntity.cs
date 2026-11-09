namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Stores a transport proxy.
/// </summary>
public sealed class TransportProxyEntity
{
    public long Id { get; set; }
    public long? LegacyConfigId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "websocket";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int ListenPort { get; set; }
    public bool OriginHttp { get; set; }
    public bool Hosted { get; set; } = true;
    public bool? UseCertificate { get; set; }
    public string HttpMethod { get; set; } = "GET";
    public int ProtocolVersion { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool UsesCertificate => UseCertificate ?? true;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsHttp => Transport is "http" or "xhttp";

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsHttpV2 => IsHttp && ProtocolVersion == 2;
    public string Path { get; set; } = string.Empty;
    public string Certificate { get; set; } = string.Empty;
    public string CertificateKey { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public string TargetsJson { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool Opened { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// Stores the identity of the proxy catalog.
/// </summary>
public sealed class ProxySourceEntity
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public int IdentityVersion { get; set; }
}
