using System.Security.Cryptography;

namespace AmneziaGeo.Server.Auth;

/// <summary>
/// A derived password with the parameters it was derived by.
/// </summary>
public sealed record StoredPassword(string Algorithm, int Iterations, byte[] Salt, byte[] Hash);

/// <summary>
/// Derives password hashes and checks passwords against them.
/// </summary>
public static class PasswordHasher
{
    public const string Algorithm = "pbkdf2-sha512";

    public const int Iterations = 210_000;

    private const int SaltLength = 16;

    private const int HashLength = 32;

    /// <summary>
    /// Derives a password with a fresh salt.
    /// </summary>
    public static StoredPassword Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);

        return new StoredPassword(Algorithm, Iterations, salt, Derive(password, salt, Iterations));
    }

    /// <summary>
    /// Tells whether a password derives to a stored hash.
    /// </summary>
    public static bool Verify(StoredPassword stored, string password)
    {
        if (stored.Algorithm != Algorithm)
        {
            return false;
        }

        var derived = Derive(password, stored.Salt, stored.Iterations);

        return CryptographicOperations.FixedTimeEquals(derived, stored.Hash);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, HashLength);
}
