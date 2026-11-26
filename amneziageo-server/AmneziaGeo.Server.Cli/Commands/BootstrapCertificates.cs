using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Checks the identity and trust chain of installation certificates.
/// </summary>
public static class BootstrapCertificates
{
    /// <summary>
    /// Validates a PEM identity and its configured names.
    /// </summary>
    public static void Check(string chainFile, string keyFile, IReadOnlyList<string> names, IReadOnlyList<string> roots, string field)
    {
        if (chainFile.Length == 0 && keyFile.Length == 0)
        {
            return;
        }

        if (chainFile.Length == 0 || keyFile.Length == 0)
        {
            throw new BootstrapFault(field, "both certificate and private-key files are required");
        }

        try
        {
            using var certificate = X509Certificate2.CreateFromPemFile(chainFile, keyFile);
            if (DateTime.UtcNow < certificate.NotBefore.ToUniversalTime())
            {
                throw new BootstrapFault(field, "certificate is not valid before " + certificate.NotBefore.ToUniversalTime().ToString("O"));
            }

            if (DateTime.UtcNow >= certificate.NotAfter.ToUniversalTime())
            {
                throw new BootstrapFault(field, "certificate expired at " + certificate.NotAfter.ToUniversalTime().ToString("O"));
            }

            var extras = new X509Certificate2Collection();
            extras.ImportFromPemFile(chainFile);
            try
            {
                if (!Trusted(certificate, extras, roots))
                {
                    throw new BootstrapFault(field, "certificate chain, validity period or server-authentication usage failed; a private CA requires validation.trustedCertificates");
                }

                foreach (var name in names)
                {
                    if (!certificate.MatchesHostname(name, allowWildcards: true, allowCommonName: false))
                    {
                        throw new BootstrapFault(field, "certificate SAN does not cover configured host " + name);
                    }
                }
            }
            finally
            {
                foreach (var extra in extras)
                {
                    extra.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new BootstrapFault(field, "certificate/key is missing, unreadable, malformed or does not match", ex);
        }
    }

    /// <summary>
    /// Checks the system trust and explicitly configured trust anchors.
    /// </summary>
    public static bool Trusted(X509Certificate2 certificate, X509Certificate2Collection extras, IReadOnlyList<string> roots)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        chain.ChainPolicy.ExtraStore.AddRange(extras);
        if (chain.Build(certificate))
        {
            return true;
        }

        if (roots.Count == 0)
        {
            return false;
        }

        var anchors = new X509Certificate2Collection();
        try
        {
            foreach (var root in roots)
            {
                anchors.ImportFromPemFile(root);
            }

            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(anchors);

            return chain.Build(certificate);
        }
        finally
        {
            foreach (var anchor in anchors)
            {
                anchor.Dispose();
            }
        }
    }
}
