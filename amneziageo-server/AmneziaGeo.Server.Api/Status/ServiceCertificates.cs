using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Api.Status;

/// <summary>
/// Reads what the file of a certificate says for the checks of the services.
/// </summary>
public static class ServiceCertificates
{
    /// <summary>
    /// Returns the name and the end of the first certificate of a chain file, or why the file did not read.
    /// </summary>
    public static CertificateFacts Read(string chain, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(chain));
            var name = certificate.GetNameInfo(X509NameType.DnsName, false);

            return new CertificateFacts(
                name.Length > 0 ? name : Folder(chain),
                new DateTimeOffset(certificate.NotAfter).ToUniversalTime(),
                string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.LogDebug(ex, "the certificate {Path} did not read", chain);

            return new CertificateFacts(Folder(chain), null, ex.Message);
        }
    }

    // Names a chain file by the folder it lies in.
    private static string Folder(string chain)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(chain) ?? string.Empty);

        return folder.Length > 0 ? folder : Path.GetFileName(chain);
    }
}
