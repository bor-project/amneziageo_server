using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class CertificateCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AChainUnderARootEveryClientTakesCarriesTheSubscriptions()
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        var verdict = CertificateCheck.Of([leaf, middle], "vpn.example.org", Now, [root]);

        Assert.Equal(CertificateVerdict.Good, verdict);
    }

    [Fact]
    public void APortWithoutAFileHoldsNoCertificateOfItsOwn()
    {
        Assert.Equal(
            new CertificateVerdict(CertificateCheck.Missing, string.Empty),
            CertificateCheck.Of(string.Empty, "vpn.example.org", Now));
    }

    [Fact]
    public void AFileThatHoldsNoCertificateOrIsNotThereDoesNotRead()
    {
        using var place = new Issuing.Place();
        var broken = Path.Combine(place.Root, "broken.pem");
        File.WriteAllText(broken, "not a certificate");

        Assert.Equal(CertificateCheck.Unreadable, CertificateCheck.Of(broken, "vpn.example.org", Now).Fault);
        Assert.Equal(CertificateCheck.Unreadable, CertificateCheck.Of(Path.Combine(place.Root, "gone.pem"), "vpn.example.org", Now).Fault);
    }

    [Fact]
    public void ACertificateThatRanOutSaysWhen()
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-91), Now.AddDays(-1));

        Assert.Equal(
            new CertificateVerdict(CertificateCheck.Expired, "2026-10-24"),
            CertificateCheck.Of([leaf, middle], "vpn.example.org", Now, [root]));
    }

    [Theory]
    [InlineData("other.example.org")]
    [InlineData("203.0.113.5")]
    [InlineData("[2001:db8::5]")]
    public void ACertificateIssuedForAnotherNameNamesTheOneItMisses(string name)
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(
            new CertificateVerdict(CertificateCheck.Name, name.Trim('[', ']')),
            CertificateCheck.Of([leaf, middle], name, Now, [root]));
    }

    [Theory]
    [InlineData("*.example.org", "vpn.example.org")]
    [InlineData("203.0.113.5", "203.0.113.5")]
    public void ACertificateCarriesTheSubscriptionsToANameItCovers(string issued, string name)
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, issued, Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(CertificateVerdict.Good, CertificateCheck.Of([leaf, middle], name, Now, [root]));
    }

    [Fact]
    public void ACertificateSignedByItselfIsNotTaken()
    {
        using var leaf = Issuing.Root("CN=vpn.example.org", Now);

        Assert.Equal(new CertificateVerdict(CertificateCheck.SelfSigned, string.Empty), CertificateCheck.Of([leaf], string.Empty, Now, [leaf]));
    }

    [Fact]
    public void AFileWithoutTheAuthorityThatIssuedTheCertificateNamesThatAuthority()
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(
            new CertificateVerdict(CertificateCheck.NoIntermediate, "an authority of the tests"),
            CertificateCheck.Of([leaf], "vpn.example.org", Now, [root]));
    }

    [Fact]
    public void ACertificateARootIssuesStraightNeedsNoOtherInTheFile()
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var leaf = Issuing.Leaf(root, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(CertificateVerdict.Good, CertificateCheck.Of([leaf], "vpn.example.org", Now, [root]));
    }

    [Fact]
    public void AChainUnderARootTheHostDoesNotTrustNamesTheRoot()
    {
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var other = Issuing.Root("CN=another root", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var leaf = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(
            new CertificateVerdict(CertificateCheck.Untrusted, "the root of the tests"),
            CertificateCheck.Of([leaf, middle], "vpn.example.org", Now, [other]));
    }

    [Fact]
    public void AChainUpToIsrgRootX2IsNotOneAndroidBefore14Takes()
    {
        using var x2 = Issuing.Root(Issuing.RootX2, Now);
        using var e5 = Issuing.Authority(x2, "CN=E5, O=Let's Encrypt, C=US", Now);
        using var leaf = Issuing.Leaf(e5, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(
            new CertificateVerdict(CertificateCheck.OldAndroid, "ISRG Root X2"),
            CertificateCheck.Of([leaf, e5], "vpn.example.org", Now, [x2]));
        Assert.Equal(
            new CertificateVerdict(CertificateCheck.OldAndroid, "ISRG Root X2"),
            CertificateCheck.Of([leaf, e5, x2], "vpn.example.org", Now, [x2]));
    }

    [Fact]
    public void AChainThatCrossesIsrgRootX2UnderX1IsTaken()
    {
        using var x1 = Issuing.Root("CN=ISRG Root X1, O=Internet Security Research Group, C=US", Now);
        using var x2Key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var x2 = Issuing.Root(Issuing.RootX2, Now, x2Key);
        using var crossed = Issuing.Authority(x1, Issuing.RootX2, Now, x2Key);
        using var e5 = Issuing.Authority(x2, "CN=E5, O=Let's Encrypt, C=US", Now);
        using var leaf = Issuing.Leaf(e5, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));

        Assert.Equal(CertificateVerdict.Good, CertificateCheck.Of([leaf, e5, crossed], "vpn.example.org", Now, [x1]));
    }

    [Fact]
    public void AVerdictIsReadAgainWhenTheFileChanges()
    {
        using var place = new Issuing.Place();
        using var root = Issuing.Root("CN=the root of the tests", Now);
        using var middle = Issuing.Authority(root, "CN=an authority of the tests", Now);
        using var good = Issuing.Leaf(middle, "vpn.example.org", Now.AddDays(-30), Now.AddDays(60));
        using var other = Issuing.Leaf(middle, "other.example.org", Now.AddDays(-30), Now.AddDays(60));
        var chain = Issuing.Write(place.Root, good, middle);
        var verdicts = new CertificateVerdicts(new FixedClock(Now), [root]);

        var first = verdicts.Of(chain, "vpn.example.org");
        Issuing.Write(place.Root, other, middle);
        File.SetLastWriteTimeUtc(chain, DateTime.UtcNow.AddMinutes(1));
        var second = verdicts.Of(chain, "vpn.example.org");

        Assert.Equal(CertificateVerdict.Good, first);
        Assert.Equal(new CertificateVerdict(CertificateCheck.Name, "vpn.example.org"), second);
    }

    [Theory]
    [InlineData("http://203.0.113.5:8080/sub/abc")]
    [InlineData("https://203.0.113.5:8443/sub/abc")]
    public void ASubscriptionReadOverPlainHttpOrUnderNoCertificateHasNoneOfItsOwn(string address)
    {
        var certificates = new SubscriptionCertificates(
            new SubscriptionState(),
            PanelDefaults.Settings,
            new WebOptions(),
            new CertificateVerdicts(),
            NullLogger<SubscriptionCertificates>.Instance);

        Assert.Equal(new CertificateVerdict(CertificateCheck.Missing, string.Empty), certificates.Of(address));
        Assert.Equal(CertificateVerdict.Good, certificates.Of(string.Empty));
    }

    // A clock that stands still at the moment given.
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

/// <summary>
/// Issues the certificates of the tests: roots, authorities under them and the certificates of names.
/// </summary>
internal static class Issuing
{
    /// <summary>
    /// The name of ISRG Root X2.
    /// </summary>
    public const string RootX2 = "CN=ISRG Root X2, O=Internet Security Research Group, C=US";

    /// <summary>
    /// Returns a root signed by itself, with its key.
    /// </summary>
    public static X509Certificate2 Root(string subject, DateTimeOffset now, ECDsa? key = null)
    {
        var held = key ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var request = Authority(subject, held);

            return request.CreateSelfSigned(now.AddYears(-2), now.AddYears(5));
        }
        finally
        {
            if (key is null)
            {
                held.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns an authority the issuer signs, with its key.
    /// </summary>
    public static X509Certificate2 Authority(X509Certificate2 issuer, string subject, DateTimeOffset now, ECDsa? key = null)
    {
        var held = key ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            using var signed = Authority(subject, held).Create(issuer, now.AddYears(-1), now.AddYears(3), Serial());

            return signed.CopyWithPrivateKey(held);
        }
        finally
        {
            if (key is null)
            {
                held.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns the certificate of a name or an address the issuer signs, with its key.
    /// </summary>
    public static X509Certificate2 Leaf(X509Certificate2 issuer, string name, DateTimeOffset from, DateTimeOffset until)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(name, out var address))
        {
            names.AddIpAddress(address);
        }
        else
        {
            names.AddDnsName(name);
        }

        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var signed = request.Create(issuer, from, until, Serial());

        return signed.CopyWithPrivateKey(key);
    }

    /// <summary>
    /// Writes the chain of a certificate and its key into a folder and returns the file of the chain.
    /// </summary>
    public static string Write(string folder, X509Certificate2 leaf, params X509Certificate2[] chain)
    {
        var path = Path.Combine(folder, "fullchain.pem");
        File.WriteAllText(path, string.Concat(new[] { leaf }.Concat(chain).Select(one => one.ExportCertificatePem() + "\n")));
        File.WriteAllText(Path.Combine(folder, "privkey.pem"), leaf.GetECDsaPrivateKey()!.ExportPkcs8PrivateKeyPem());

        return path;
    }

    private static CertificateRequest Authority(string subject, ECDsa key)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request;
    }

    private static byte[] Serial() => RandomNumberGenerator.GetBytes(16);

    /// <summary>
    /// A folder of its own that goes away after the test.
    /// </summary>
    public sealed class Place : IDisposable
    {
        /// <summary>
        /// ctor
        /// </summary>
        public Place()
        {
            Root = Path.Combine(Path.GetTempPath(), $"amneziageo-certificate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        /// <summary>
        /// The folder.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Removes the folder.
        /// </summary>
        public void Dispose()
        {
            Directory.Delete(Root, true);
        }
    }
}
