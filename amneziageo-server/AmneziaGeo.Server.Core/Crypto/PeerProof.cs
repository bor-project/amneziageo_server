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
    /// What the countersign of the server is bound to.
    /// </summary>
    public const string ServerContext = "amneziageo-server";

    /// <summary>
    /// How many bytes a challenge carries.
    /// </summary>
    public const int ChallengeBytes = 32;

    /// <summary>
    /// How many bytes the nonce of a client carries.
    /// </summary>
    public const int NonceBytes = 32;

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

    /// <summary>
    /// Tells whether a nonce is the base64 of the bytes a client sends.
    /// </summary>
    public static bool IsNonce(string? nonce)
    {
        if (nonce is not { Length: > 0 })
        {
            return false;
        }

        var bytes = new byte[NonceBytes + 3];

        return Convert.TryFromBase64String(nonce, bytes, out var written) && written == NonceBytes;
    }

    /// <summary>
    /// Returns the countersign of an answer body from the private key of one side and the public key of the other.
    /// </summary>
    public static string Countersign(string privateKey, string publicKey, string nonce, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(nonce);

        var shared = Curve25519.Product(Curve25519.Bytes(privateKey), Curve25519.Bytes(publicKey));
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, shared);
        hmac.AppendData(Encoding.UTF8.GetBytes(ServerContext + nonce));
        hmac.AppendData(body);

        return Convert.ToBase64String(hmac.GetHashAndReset());
    }
}
