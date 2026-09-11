namespace AmneziaGeo.Server.Auth;

/// <summary>
/// The shape a long lived token and its settings are held to.
/// </summary>
public static class ApiTokenRules
{
    /// <summary>
    /// The start every long lived token carries.
    /// </summary>
    public const string Prefix = "agt_";

    public const int MaxNameLength = 64;

    public const int MaxDays = 3650;

    /// <summary>
    /// Tells whether a bearer value is a long lived token.
    /// </summary>
    public static bool Looks(string token) => token.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Returns why a token name is refused, or null when it is taken.
    /// </summary>
    public static string? CheckName(string name) =>
        name.Length is 0 or > MaxNameLength || name.Any(char.IsControl)
            ? $"a token name takes 1 to {MaxNameLength} characters"
            : null;

    /// <summary>
    /// Returns why a lifetime in days is refused, or null when it is taken.
    /// </summary>
    public static string? CheckDays(int? days) =>
        days is < 1 or > MaxDays ? $"a token lives 1 to {MaxDays} days or has no end" : null;
}
