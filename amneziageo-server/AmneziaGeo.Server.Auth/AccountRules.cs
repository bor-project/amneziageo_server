using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// Shapes a login name and a password have to take.
/// </summary>
public static partial class AccountRules
{
    public const int MinPasswordLength = 8;

    public const int MaxNameLength = 32;

    /// <summary>
    /// Returns why a login name is unusable, or null when it holds.
    /// </summary>
    public static string? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "the name is empty";
        }

        if (name.Length > MaxNameLength)
        {
            return $"the name is longer than {MaxNameLength} characters";
        }

        if (!NameShape().IsMatch(name))
        {
            return "the name takes lower case letters, digits, dot, dash and underscore, and starts with a letter";
        }

        return null;
    }

    /// <summary>
    /// Returns why a password is unusable, or null when it holds.
    /// </summary>
    public static string? CheckPassword(string? password, int minimum = MinPasswordLength)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "the password is empty";
        }

        if (password.Length < minimum)
        {
            return $"the password is shorter than {minimum} characters";
        }

        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9._-]*$")]
    private static partial Regex NameShape();
}
