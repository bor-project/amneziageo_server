using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The certificate the panel answers under, reloaded when the files behind it change.
/// </summary>
public sealed class WebCertificate
{
    private static readonly Lazy<X509Certificate2> Made = new(Make);

    private readonly string _chain;

    private readonly string _key;

    private readonly Lock _sync = new();

    private X509Certificate2? _held;

    private string _stamp = "";

    /// <summary>
    /// ctor
    /// </summary>
    public WebCertificate(string chain, string key)
    {
        _chain = chain;
        _key = key;
    }

    /// <summary>
    /// ctor
    /// </summary>
    private WebCertificate()
    {
        _chain = string.Empty;
        _key = string.Empty;
    }

    /// <summary>
    /// Returns the certificate the panel answers under.
    /// </summary>
    public X509Certificate2 Current()
    {
        if (_chain.Length == 0)
        {
            return Made.Value;
        }

        lock (_sync)
        {
            var chain = Read(_chain);
            var key = Read(_key);
            var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chain + "\0" + key)));
            if (_held is null || stamp != _stamp)
            {
                _held = X509Certificate2.CreateFromPem(chain, key);
                _stamp = stamp;
            }

            return _held;
        }
    }

    /// <summary>
    /// Returns the certificate the chain and the key name, or null when no chain is named.
    /// </summary>
    public static WebCertificate? Of(string chain, string key)
    {
        if (chain.Length == 0)
        {
            return null;
        }

        if (!File.Exists(chain))
        {
            throw new InvalidOperationException("the panel answers under a certificate that is not there: " + chain);
        }

        var held = key.Length > 0 ? key : chain;
        if (!File.Exists(held))
        {
            throw new InvalidOperationException("the certificate of the panel has no key at " + held);
        }

        var certificate = new WebCertificate(chain, held);
        certificate.Current();

        return certificate;
    }

    /// <summary>
    /// Returns a certificate made up for a port that answers under none of its own.
    /// </summary>
    public static WebCertificate MadeUp() => new();

    /// <summary>
    /// Reads a certificate file.
    /// </summary>
    internal static string Read(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[64 * 1024 + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count == buffer.Length)
        {
            throw new InvalidDataException("the certificate file exceeds its size limit");
        }

        return new string(buffer, 0, count);
    }

    // Makes up the certificate a port answers under when the panel holds none.
    private static X509Certificate2 Make()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var made = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
    }
}
