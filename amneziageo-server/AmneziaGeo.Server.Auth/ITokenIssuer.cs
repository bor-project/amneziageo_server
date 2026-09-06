namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Issues and checks signed access tokens.
/// </summary>
public interface ITokenIssuer
{
    /// <summary>
    /// Signs an access token for a principal.
    /// </summary>
    string Issue(Principal principal, DateTimeOffset now);

    /// <summary>
    /// Reads a token back, returning null when the signature, lifetime or session does not hold.
    /// </summary>
    Principal? Read(string token, DateTimeOffset now);
}
