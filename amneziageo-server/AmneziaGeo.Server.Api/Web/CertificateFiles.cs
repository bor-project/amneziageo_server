using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Core.Panel;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Checks the files of the certificate the panel is told to answer under.
/// </summary>
public static class CertificateFiles
{
    private const int Limit = 64 * 1024;

    private static readonly Part Chain = new("certificate", "certificate file");

    private static readonly Part Key = new("certificate-key", "key file");

    private static readonly string[] PlainKeys = ["PRIVATE KEY", "EC PRIVATE KEY", "RSA PRIVATE KEY"];

    private static readonly string[] LockedKeys = ["ENCRYPTED PRIVATE KEY"];

    /// <summary>
    /// Returns what keeps the chain and the key from loading as a certificate, or null when they load.
    /// </summary>
    public static PanelFault? Check(string chain, string key, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (chain.Length == 0 || key.Length == 0)
        {
            return null;
        }

        var (chainFault, chainText) = Read(Chain, chain, logger);
        if (chainFault is not null)
        {
            return chainFault;
        }

        var certificateFault = Certificate(chainText, logger);
        if (certificateFault is not null)
        {
            return certificateFault;
        }

        var (keyFault, keyText) = Read(Key, key, logger);
        if (keyFault is not null)
        {
            return keyFault;
        }

        return PrivateKey(keyText, logger) ?? Match(chainText, keyText, logger);
    }

    private static (PanelFault? Fault, string Text) Read(Part part, string path, ILogger logger)
    {
        if (Directory.Exists(path))
        {
            return (part.Fault("is-folder", "is a folder, not a file"), string.Empty);
        }

        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[Limit];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, count);
            if (string.IsNullOrWhiteSpace(text))
            {
                return (part.Fault("empty", "is empty"), string.Empty);
            }

            return (null, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "the {Part} of the panel does not read from {Path}", part.Name, path);

            return (Blame(part, ex), string.Empty);
        }
    }

    private static PanelFault Blame(Part part, Exception ex)
    {
        return ex switch
        {
            DirectoryNotFoundException => part.Fault("no-folder", "sits in a folder that does not exist"),
            FileNotFoundException => part.Fault("not-found", "is not there"),
            UnauthorizedAccessException => part.Fault("denied", "denies access"),
            _ => part.Fault("unreadable", "does not read"),
        };
    }

    private static PanelFault? Certificate(string chain, ILogger logger)
    {
        try
        {
            X509Certificate2.CreateFromPem(chain).Dispose();
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "the certificate file of the panel holds no certificate");

            return Chain.Fault("invalid", "holds no valid certificate");
        }

        return null;
    }

    private static PanelFault? PrivateKey(string key, ILogger logger)
    {
        var block = Block(key, PlainKeys);
        if (block.Length == 0)
        {
            return Block(key, LockedKeys).Length > 0
                ? Key.Fault("encrypted", "holds a private key locked by a password")
                : Key.Fault("invalid", "holds no valid private key");
        }

        using var rsa = RSA.Create();
        using var ecdsa = ECDsa.Create();
        var refusal = Refusal(rsa, block);
        if (refusal is not null)
        {
            refusal = Refusal(ecdsa, block);
        }

        if (refusal is null)
        {
            return null;
        }

        logger.LogWarning(refusal, "the key file of the panel holds no private key");

        return Key.Fault("invalid", "holds no valid private key");
    }

    private static PanelFault? Match(string chain, string key, ILogger logger)
    {
        try
        {
            X509Certificate2.CreateFromPem(chain, key).Dispose();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            logger.LogWarning(ex, "the private key of the panel does not go with its certificate");

            return Key.Fault("mismatch", "holds a private key of another certificate");
        }

        return null;
    }

    private static Exception? Refusal(AsymmetricAlgorithm algorithm, string block)
    {
        try
        {
            algorithm.ImportFromPem(block);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return ex;
        }

        return null;
    }

    private static string Block(string text, string[] labels)
    {
        var rest = text.AsSpan();
        while (PemEncoding.TryFind(rest, out var fields))
        {
            if (labels.Contains(rest[fields.Label].ToString()))
            {
                return rest[fields.Location].ToString();
            }

            rest = rest[fields.Location.End..];
        }

        return string.Empty;
    }

    private sealed record Part(string Code, string Name)
    {
        public PanelFault Fault(string what, string message)
        {
            return new PanelFault($"{Code}-{what}", $"the {Name} of the panel {message}");
        }
    }
}
