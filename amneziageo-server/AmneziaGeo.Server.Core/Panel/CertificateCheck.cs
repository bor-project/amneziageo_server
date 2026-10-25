using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What keeps a certificate from carrying the subscriptions, empty when nothing does.
/// </summary>
/// <param name="Fault">What is wrong with the certificate, as the panel names it; empty for a certificate that carries them.</param>
/// <param name="Detail">The date, the name or the authority the fault is about.</param>
public sealed record CertificateVerdict(string Fault, string Detail)
{
    /// <summary>
    /// The verdict of a certificate that carries the subscriptions.
    /// </summary>
    public static readonly CertificateVerdict Good = new(string.Empty, string.Empty);

    /// <summary>
    /// Tells whether the certificate carries the subscriptions.
    /// </summary>
    public bool IsGood => Fault.Length == 0;
}

/// <summary>
/// Tells whether a certificate is one every client of the server takes.
/// </summary>
public static class CertificateCheck
{
    /// <summary>
    /// The port answers under no certificate of its own: one made up, or none at all.
    /// </summary>
    public const string Missing = "certificate-missing";

    /// <summary>
    /// The file of the certificate does not read.
    /// </summary>
    public const string Unreadable = "certificate-unreadable";

    /// <summary>
    /// The certificate ran out.
    /// </summary>
    public const string Expired = "certificate-expired";

    /// <summary>
    /// The certificate is not issued for the name its clients reach it at.
    /// </summary>
    public const string Name = "certificate-name";

    /// <summary>
    /// The certificate is signed by itself.
    /// </summary>
    public const string SelfSigned = "certificate-self-signed";

    /// <summary>
    /// The file holds the certificate without the one that issued it.
    /// </summary>
    public const string NoIntermediate = "certificate-no-intermediate";

    /// <summary>
    /// The chain ends at an authority the host does not trust.
    /// </summary>
    public const string Untrusted = "certificate-untrusted";

    /// <summary>
    /// The chain ends at ISRG Root X2, which Android before 14 does not carry.
    /// </summary>
    public const string OldAndroid = "certificate-old-android";

    private const string RootX2 = "ISRG Root X2";

    private static readonly Lazy<bool> HostTrusts = new(HostHoldsRoots);

    /// <summary>
    /// Returns what keeps the chain in a file from carrying the subscriptions to a name, the missing verdict without a
    /// file; the roots stand in for the ones of the host.
    /// </summary>
    public static CertificateVerdict Of(string chain, string name, DateTimeOffset now, X509Certificate2Collection? roots = null)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(name);

        if (chain.Length == 0)
        {
            return new CertificateVerdict(Missing, string.Empty);
        }

        var certificates = new X509Certificate2Collection();
        try
        {
            certificates.ImportFromPemFile(chain);

            return Of(certificates, name, now, roots);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new CertificateVerdict(Unreadable, ex.Message);
        }
        finally
        {
            foreach (var one in certificates)
            {
                one.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns what keeps a chain, the certificate first, from carrying the subscriptions to a name; an empty name is
    /// not checked, the roots stand in for the ones of the host.
    /// </summary>
    public static CertificateVerdict Of(X509Certificate2Collection certificates, string name, DateTimeOffset now, X509Certificate2Collection? roots = null)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(name);

        if (certificates.Count == 0)
        {
            return new CertificateVerdict(Unreadable, "the file holds no certificate");
        }

        var leaf = certificates[0];
        var until = new DateTimeOffset(leaf.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        if (until <= now)
        {
            return new CertificateVerdict(Expired, until.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var host = name.Trim('[', ']');
        if (host.Length > 0 && !leaf.MatchesHostname(host, allowWildcards: true, allowCommonName: false))
        {
            return new CertificateVerdict(Name, host);
        }

        if (SignsItself(leaf))
        {
            return new CertificateVerdict(SelfSigned, string.Empty);
        }

        var top = Top(leaf, certificates);
        if (ReferenceEquals(top, leaf) && !Rooted(leaf, roots))
        {
            return new CertificateVerdict(NoIntermediate, leaf.GetNameInfo(X509NameType.SimpleName, true));
        }

        if ((roots is not null || HostTrusts.Value) && !Trusted(leaf, certificates, now, roots))
        {
            return new CertificateVerdict(Untrusted, top.GetNameInfo(X509NameType.SimpleName, !SignsItself(top)));
        }

        var root = SignsItself(top) ? top.GetNameInfo(X509NameType.SimpleName, false) : top.GetNameInfo(X509NameType.SimpleName, true);

        return string.Equals(root, RootX2, StringComparison.Ordinal)
            ? new CertificateVerdict(OldAndroid, RootX2)
            : CertificateVerdict.Good;
    }

    // Tells whether a certificate is signed by itself.
    private static bool SignsItself(X509Certificate2 certificate) =>
        certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

    // Climbs the file from the certificate to the last one it holds of the chain.
    private static X509Certificate2 Top(X509Certificate2 leaf, X509Certificate2Collection certificates)
    {
        var top = leaf;
        for (var step = 0; step < certificates.Count && !SignsItself(top); step++)
        {
            var above = certificates.Cast<X509Certificate2>()
                .FirstOrDefault(one => !ReferenceEquals(one, top) && one.SubjectName.RawData.AsSpan().SequenceEqual(top.IssuerName.RawData));
            if (above is null)
            {
                break;
            }

            top = above;
        }

        return top;
    }

    // Tells whether a certificate is issued straight by a root, so no other certificate goes with it.
    private static bool Rooted(X509Certificate2 leaf, X509Certificate2Collection? roots)
    {
        if (roots is not null)
        {
            return roots.Cast<X509Certificate2>().Any(one => one.SubjectName.RawData.AsSpan().SequenceEqual(leaf.IssuerName.RawData));
        }

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, leaf.Issuer, false);
            var rooted = found.Count > 0;
            foreach (var one in found)
            {
                one.Dispose();
            }

            return rooted;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // Tells whether the chain of the file ends at a root the host or the roots given trust.
    private static bool Trusted(X509Certificate2 leaf, X509Certificate2Collection certificates, DateTimeOffset now, X509Certificate2Collection? roots)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        foreach (var one in certificates.Cast<X509Certificate2>().Where(one => !ReferenceEquals(one, leaf)))
        {
            chain.ChainPolicy.ExtraStore.Add(one);
        }

        if (roots is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        }

        try
        {
            return chain.Build(leaf);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // Tells whether the host carries any root to trust a chain by.
    private static bool HostHoldsRoots()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var held = store.Certificates;
            var any = held.Count > 0;
            foreach (var one in held)
            {
                one.Dispose();
            }

            return any;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
