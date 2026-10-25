using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Updates.Host;

/// <summary>
/// The certificate the service answers under: the one its files name, reloaded when they change, or one of its own
/// kept beside the token.
/// </summary>
public sealed class HostCertificate
{
    private const string OwnChain = "self-chain.pem";

    private const string OwnKey = "self-key.pem";

    private readonly string _chain;

    private readonly string _key;

    private readonly Lock _sync = new();

    private X509Certificate2? _held;

    private DateTime _stamp;

    private HostCertificate(string chain, string key)
    {
        _chain = chain;
        _key = key;
    }

    /// <summary>
    /// Returns the certificate the service answers under.
    /// </summary>
    public X509Certificate2 Current()
    {
        lock (_sync)
        {
            var stamp = File.GetLastWriteTimeUtc(_chain);
            if (_held is null || stamp != _stamp)
            {
                _held = X509CertificateLoader.LoadPkcs12(X509Certificate2.CreateFromPemFile(_chain, _key).Export(X509ContentType.Pkcs12), null);
                _stamp = stamp;
            }

            return _held;
        }
    }

    /// <summary>
    /// Returns the certificate the options name, or one of the service's own, made and kept in its directory when it
    /// is not there yet.
    /// </summary>
    public static HostCertificate Of(UpdatesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Certificate.Length > 0)
        {
            var key = options.CertificateKey.Length > 0 ? options.CertificateKey : options.Certificate;
            if (!File.Exists(options.Certificate) || !File.Exists(key))
            {
                throw new InvalidOperationException($"the certificate {options.Certificate} or its key {key} is not there");
            }

            return new HostCertificate(options.Certificate, key);
        }

        Directory.CreateDirectory(options.Etc);
        var chain = Path.Combine(options.Etc, OwnChain);
        var own = Path.Combine(options.Etc, OwnKey);
        if (!File.Exists(chain) || !File.Exists(own))
        {
            Make(chain, own);
        }

        return new HostCertificate(chain, own);
    }

    // Makes a certificate of the service's own for ten years and writes it with its key.
    private static void Make(string chain, string key)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=amneziageo-updates", ecdsa, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var made = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        File.WriteAllText(key, ecdsa.ExportPkcs8PrivateKeyPem() + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(key, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.WriteAllText(chain, made.ExportCertificatePem() + "\n");
    }
}
