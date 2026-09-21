using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AmneziaGeo.Server.Core.Crypto;

/// <summary>
/// An answer sealed under the secret a client and its endpoint share.
/// </summary>
/// <param name="Iv">The nonce of the cipher, in base64.</param>
/// <param name="Data">The sealed bytes with the tag after them, in base64.</param>
public sealed record SealedAnswer(string Iv, string Data);

/// <summary>
/// Counts the token a client proves the keys of its configuration with and seals what it is answered.
/// </summary>
public static class PeerToken
{
    /// <summary>
    /// What the proof of a token is bound to.
    /// </summary>
    public const string Context = "amneziageo-hello";

    /// <summary>
    /// What the key of a sealed answer is bound to.
    /// </summary>
    public const string ReplyContext = "amneziageo-reply";

    /// <summary>
    /// The scheme a token travels under in the header of a websocket.
    /// </summary>
    public const string Scheme = "AmneziaGeo";

    /// <summary>
    /// How many bytes the nonce of a token carries.
    /// </summary>
    public const int NonceBytes = 16;

    /// <summary>
    /// How many bytes the nonce of the cipher carries.
    /// </summary>
    public const int IvBytes = 12;

    /// <summary>
    /// How many bytes the tag of a sealed answer carries.
    /// </summary>
    public const int TagBytes = 16;

    /// <summary>
    /// How far the time of a token may stand from the clock of the server.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns the secret the private key of one side and the public key of the other share.
    /// </summary>
    public static byte[] Shared(string privateKey, string publicKey) =>
        Curve25519.Product(Curve25519.Bytes(privateKey), Curve25519.Bytes(publicKey));

    /// <summary>
    /// Returns fresh random bytes for a token, in base64.
    /// </summary>
    public static string Nonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

    /// <summary>
    /// Returns the proof of a token.
    /// </summary>
    public static string Proof(byte[] shared, string key, long time, string nonce) =>
        Convert.ToBase64String(HMACSHA256.HashData(shared, Encoding.UTF8.GetBytes(Message(key, time, nonce))));

    /// <summary>
    /// Returns the header a websocket proves the keys of its configuration with.
    /// </summary>
    public static string Header(string privateKey, string publicKey, DateTimeOffset now)
    {
        var key = Curve25519.PublicOf(privateKey);
        var time = now.ToUnixTimeSeconds();
        var nonce = Nonce();
        var proof = Proof(Shared(privateKey, publicKey), key, time, nonce);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { key, time, nonce, proof });

        return $"{Scheme} {Convert.ToBase64String(body).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    /// <summary>
    /// Tells whether the proof of a token comes from the shared secret.
    /// </summary>
    public static bool Holds(byte[] shared, string key, long time, string nonce, string? proof)
    {
        if (proof is not { Length: > 0 })
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Proof(shared, key, time, nonce)),
            Encoding.UTF8.GetBytes(proof));
    }

    /// <summary>
    /// Tells whether a nonce is the base64 of the bytes a token carries.
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
    /// Tells whether the time of a token stands within the window around a moment.
    /// </summary>
    public static bool Fresh(long time, DateTimeOffset now) =>
        Math.Abs(now.ToUnixTimeSeconds() - time) <= (long)Window.TotalSeconds;

    /// <summary>
    /// Seals an answer under a key counted from the shared secret and the nonce of the token.
    /// </summary>
    public static SealedAnswer Seal(byte[] shared, string nonce, ReadOnlySpan<byte> body)
    {
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var output = new byte[body.Length + TagBytes];
        using var cipher = new AesGcm(ReplyKey(shared, nonce), TagBytes);
        cipher.Encrypt(iv, body, output.AsSpan(0, body.Length), output.AsSpan(body.Length));

        return new SealedAnswer(Convert.ToBase64String(iv), Convert.ToBase64String(output));
    }

    /// <summary>
    /// Opens a sealed answer, or returns null when it was not sealed under that secret and nonce.
    /// </summary>
    public static byte[]? Open(byte[] shared, string nonce, SealedAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        try
        {
            var iv = Convert.FromBase64String(answer.Iv);
            var data = Convert.FromBase64String(answer.Data);
            if (iv.Length != IvBytes || data.Length < TagBytes)
            {
                return null;
            }

            var body = new byte[data.Length - TagBytes];
            using var cipher = new AesGcm(ReplyKey(shared, nonce), TagBytes);
            cipher.Decrypt(iv, data.AsSpan(0, body.Length), data.AsSpan(body.Length), body);

            return body;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static byte[] ReplyKey(byte[] shared, string nonce) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Convert.FromBase64String(nonce), Encoding.UTF8.GetBytes(ReplyContext));

    private static string Message(string key, long time, string nonce) =>
        string.Create(CultureInfo.InvariantCulture, $"{Context}\n{key}\n{time}\n{nonce}");
}
