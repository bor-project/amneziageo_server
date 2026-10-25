using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace AmneziaGeo.Server.Updates;

/// <summary>
/// The token that lets the releases be changed.
/// </summary>
public static class UpdateToken
{
    private const int Bytes = 24;

    /// <summary>
    /// Makes a new token.
    /// </summary>
    public static string Make() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>
    /// Tells whether a request carries the token as a bearer.
    /// </summary>
    public static bool Holds(HttpContext context, string token)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(token);

        if (token.Length == 0 || context.Request.Headers.Authorization.ToString() is not { Length: > 7 } header)
        {
            return false;
        }

        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..].Trim()), Encoding.UTF8.GetBytes(token));
    }
}
