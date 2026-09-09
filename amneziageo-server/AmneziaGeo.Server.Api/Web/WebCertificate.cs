using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// The certificate the panel answers under, reloaded when the files behind it change.
/// </summary>
public sealed class WebCertificate
{
    private readonly string _chain;

    private readonly string _key;

    private readonly Lock _sync = new();

    private X509Certificate2? _held;

    private DateTime _stamp;

    /// <summary>
    /// ctor
    /// </summary>
    public WebCertificate(string chain, string key)
    {
        _chain = chain;
        _key = key;
    }

    /// <summary>
    /// Returns the certificate the panel answers under.
    /// </summary>
    public X509Certificate2 Current()
    {
        lock (_sync)
        {
            var stamp = File.GetLastWriteTimeUtc(_chain);
            if (_held is null || stamp != _stamp)
            {
                _held = X509Certificate2.CreateFromPemFile(_chain, _key);
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
}
