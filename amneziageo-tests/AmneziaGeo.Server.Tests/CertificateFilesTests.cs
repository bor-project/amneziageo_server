using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmneziaGeo.Server.Tests;

public sealed class CertificateFilesTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("amneziageo-tls-");

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void APairThatLoadsIsTaken()
    {
        var (chain, key) = Pair("stand");

        Assert.Null(Check(chain, key));
    }

    [Fact]
    public void AnRsaPairIsTaken()
    {
        using var rsa = RSA.Create(2048);
        var chain = Write("rsa.pem", Issue(new CertificateRequest("CN=rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        var key = Write("rsa.key", rsa.ExportRSAPrivateKeyPem());

        Assert.Null(Check(chain, key));
    }

    [Fact]
    public void AnEcKeyOfItsOwnFormIsTaken()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = Write("ec.pem", Issue(new CertificateRequest("CN=ec", ecdsa, HashAlgorithmName.SHA256)));
        var key = Write("ec.key", ecdsa.ExportECPrivateKeyPem());

        Assert.Null(Check(chain, key));
    }

    [Fact]
    public void NoPairIsTaken()
    {
        Assert.Null(Check(string.Empty, string.Empty));
    }

    [Fact]
    public void AFolderThatIsNotThereIsNamed()
    {
        var (chain, key) = Pair("stand");
        var nowhere = Path.Combine(_folder.FullName, "nowhere", "file.pem");

        Assert.Equal("certificate-no-folder", Check(nowhere, key));
        Assert.Equal("certificate-key-no-folder", Check(chain, nowhere));
    }

    [Fact]
    public void AFileThatIsNotThereIsNamed()
    {
        var (chain, key) = Pair("stand");
        var missing = Path.Combine(_folder.FullName, "missing.pem");

        Assert.Equal("certificate-not-found", Check(missing, key));
        Assert.Equal("certificate-key-not-found", Check(chain, missing));
    }

    [Fact]
    public void AFolderInPlaceOfAFileIsNamed()
    {
        var (chain, key) = Pair("stand");

        Assert.Equal("certificate-is-folder", Check(_folder.FullName, key));
        Assert.Equal("certificate-key-is-folder", Check(chain, _folder.FullName));
    }

    [Fact]
    public void AFileClosedToThePanelIsNamed()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            return;
        }

        var (chain, key) = Pair("stand");
        var (closedChain, closedKey) = Pair("closed");
        File.SetUnixFileMode(closedChain, UnixFileMode.None);
        File.SetUnixFileMode(closedKey, UnixFileMode.None);

        Assert.Equal("certificate-denied", Check(closedChain, key));
        Assert.Equal("certificate-key-denied", Check(chain, closedKey));
    }

    [Fact]
    public void EmptyFilesAreNamed()
    {
        var (chain, key) = Pair("stand");
        var empty = Write("empty.pem", string.Empty);

        Assert.Equal("certificate-empty", Check(empty, key));
        Assert.Equal("certificate-key-empty", Check(chain, empty));
    }

    [Fact]
    public void AFileWithoutACertificateIsNamed()
    {
        var (_, key) = Pair("stand");

        Assert.Equal("certificate-invalid", Check(key, key));
    }

    [Fact]
    public void AFileWithoutAKeyIsNamed()
    {
        var (chain, _) = Pair("stand");
        var broken = Write("broken.key", "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n");

        Assert.Equal("certificate-key-invalid", Check(chain, chain));
        Assert.Equal("certificate-key-invalid", Check(chain, broken));
    }

    [Fact]
    public void AKeyLockedByAPasswordIsNamed()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = Write("locked.pem", Issue(new CertificateRequest("CN=locked", ecdsa, HashAlgorithmName.SHA256)));
        var parameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
        var key = Write("locked.key", ecdsa.ExportEncryptedPkcs8PrivateKeyPem("secret", parameters));

        Assert.Equal("certificate-key-encrypted", Check(chain, key));
    }

    [Fact]
    public void TheKeyOfAnotherCertificateIsNamed()
    {
        var (chain, _) = Pair("one");
        var (_, key) = Pair("two");

        Assert.Equal("certificate-key-mismatch", Check(chain, key));
    }

    private static string? Check(string chain, string key)
    {
        return CertificateFiles.Check(chain, key, NullLogger.Instance)?.Code;
    }

    private static string Issue(CertificateRequest request)
    {
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        return certificate.ExportCertificatePem();
    }

    private (string Chain, string Key) Pair(string name)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var chain = Write(name + ".pem", Issue(new CertificateRequest("CN=" + name, ecdsa, HashAlgorithmName.SHA256)));
        var key = Write(name + ".key", ecdsa.ExportPkcs8PrivateKeyPem());

        return (chain, key);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllText(path, text);

        return path;
    }
}
