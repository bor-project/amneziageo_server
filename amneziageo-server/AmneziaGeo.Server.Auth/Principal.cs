namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The caller behind a request, whatever scheme carried it.
/// </summary>
public sealed record Principal(
    long Id,
    string Name,
    AuthScheme Scheme,
    long SessionId,
    IReadOnlySet<string> Scopes,
    string Role = "")
{
    /// <summary>
    /// Tells whether the caller holds a right.
    /// </summary>
    public bool Holds(string scope) => Scopes.Contains(scope);
}
