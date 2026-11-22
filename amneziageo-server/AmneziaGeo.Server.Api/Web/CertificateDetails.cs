using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Describes a public certificate.
/// </summary>
public sealed record CertificateDetails(string Name, string Subject, string Issuer,
    IReadOnlyList<string> SubjectAlternativeNames, DateTimeOffset NotBefore, DateTimeOffset NotAfter,
    string KeyAlgorithm, string KeyOid, int? KeyBits, string SignatureAlgorithm, string Sha256)
{
    /// <summary>
    /// Reads public certificate details.
    /// </summary>
    public static CertificateDetails Of(X509Certificate2 certificate)
    {
        return new CertificateDetails(certificate.GetNameInfo(X509NameType.DnsName, false), certificate.Subject,
            certificate.Issuer, Names(certificate), new DateTimeOffset(certificate.NotBefore.ToUniversalTime()),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), certificate.PublicKey.Oid.FriendlyName ?? "",
            certificate.PublicKey.Oid.Value ?? "", Bits(certificate),
            certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? "",
            certificate.GetCertHashString(HashAlgorithmName.SHA256));
    }

    private static int? Bits(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        using var ec = certificate.GetECDsaPublicKey();
        using var dsa = certificate.GetDSAPublicKey();
        return rsa?.KeySize ?? ec?.KeySize ?? dsa?.KeySize;
    }

    private static IReadOnlyList<string> Names(X509Certificate2 certificate)
    {
        if (certificate.Extensions["2.5.29.17"] is not { } extension)
        {
            return [];
        }

        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
        var names = reader.ReadSequence();
        var result = new List<string>();
        while (names.HasData)
        {
            var tag = names.PeekTag();
            if (tag.TagClass != TagClass.ContextSpecific)
            {
                throw new CryptographicException("Invalid subject alternative name.");
            }
            var value = tag.TagValue switch
            {
                1 => "email:" + names.ReadCharacterString(UniversalTagNumber.IA5String, tag),
                2 => "DNS:" + names.ReadCharacterString(UniversalTagNumber.IA5String, tag),
                6 => "URI:" + names.ReadCharacterString(UniversalTagNumber.IA5String, tag),
                7 => "IP:" + new IPAddress(names.ReadOctetString(tag)),
                _ => "[" + tag.TagValue + "]:" + Convert.ToHexString(names.ReadEncodedValue().Span),
            };
            result.Add(value);
        }
        reader.ThrowIfNotEmpty();
        return result;
    }
}
