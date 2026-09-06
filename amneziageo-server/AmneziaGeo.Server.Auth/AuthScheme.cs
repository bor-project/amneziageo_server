namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The way a caller proved who it is.
/// </summary>
public enum AuthScheme
{
    None = 0,
    ApiToken = 1,
    OAuth = 2,
    Bearer = 3,
    ClientCertificate = 4,
    Password = 5,
    HostUser = 6,
}
