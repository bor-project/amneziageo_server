using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Core.Crypto;

/// <summary>
/// Proves that a caller holds the private key of a peer, without the key leaving the caller.
/// </summary>
public static class PeerProof
{
    /// <summary>
    /// What the proof is bound to, so the shared secret answers for nothing else.
    /// </summary>
    public const string Context = "amneziageo-hello";

    /// <summary>
    /// How many bytes a challenge carries.
    /// </summary>
    public const int ChallengeBytes = 32;

    /// <summary>
    /// Returns a challenge for a caller to answer.
    /// </summary>
    public static string Challenge() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(ChallengeBytes));

    /// <summary>
    /// Returns the answer to a challenge from the private key of one side and the public key of the other.
    /// </summary>
    public static string Answer(string privateKey, string publicKey, string challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var shared = Curve25519.Product(Curve25519.Bytes(privateKey), Curve25519.Bytes(publicKey));

        return Convert.ToBase64String(HMACSHA256.HashData(shared, Encoding.UTF8.GetBytes(Context + challenge)));
    }

    /// <summary>
    /// Tells whether an answer is the one the keys and the challenge produce.
    /// </summary>
    public static bool Holds(string privateKey, string publicKey, string challenge, string? answer)
    {
        if (answer is not { Length: > 0 }
            || !Curve25519.IsKey(privateKey)
            || !Curve25519.IsKey(publicKey))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Answer(privateKey, publicKey, challenge)),
            Encoding.UTF8.GetBytes(answer));
    }
}
