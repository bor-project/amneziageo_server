using System.Security.Cryptography;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Checks that a manifest was signed with the key of the releases.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// The public key the releases of the panel are signed with.
    /// </summary>
    public const string Published = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEzgZ6s+M0wvE0HMsFuPhwk7cZsCSY
        vbaWhP248NSthVEmlp9mRRjOf9zIFsIZ5P4mM1ETGo+l/Gb+S+7TcwVUew==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// The largest signature the panel reads, in bytes.
    /// </summary>
    public const int MaxSize = 1024;

    /// <summary>
    /// Returns the public key manifests are checked against, empty when the panel holds none.
    /// </summary>
    public static string Key(UpdateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Key.Length > 0 ? File.ReadAllText(options.Key) : Published;
    }

    /// <summary>
    /// Tells whether a signature over data was made with the private half of a key.
    /// </summary>
    public static bool Holds(byte[] data, byte[] signature, string key)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportFromPem(key);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException("the key of the releases does not read as an EC public key in PEM", ex);
        }

        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
}
