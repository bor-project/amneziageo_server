using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Describes the certificate chain of the running panel.
/// </summary>
public sealed record PanelCertificateResponse(bool Enabled, IReadOnlyList<CertificateDetails> Certificates);

/// <summary>
/// Reads the certificate identity of the running panel.
/// </summary>
public sealed class PanelCertificates
{
    private readonly WebCertificate? _certificate;

    /// <summary>
    /// ctor
    /// </summary>
    public PanelCertificates(WebCertificate? certificate)
    {
        _certificate = certificate;
    }

    /// <summary>
    /// Exports the public certificate chain.
    /// </summary>
    public IReadOnlyList<string> PublicChain() => _certificate?.PublicChain() ?? [];

    /// <summary>
    /// Returns the public certificate details.
    /// </summary>
    public PanelCertificateResponse Read()
    {
        var details = PublicChain().Select(pem =>
        {
            using var certificate = X509Certificate2.CreateFromPem(pem);
            return CertificateDetails.Of(certificate);
        }).ToArray();
        return new PanelCertificateResponse(details.Length > 0, details);
    }
}
