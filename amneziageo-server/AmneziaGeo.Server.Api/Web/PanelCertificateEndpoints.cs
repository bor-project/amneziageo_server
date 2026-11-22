using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Auth;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Exposes the public certificate of the running panel.
/// </summary>
public static class PanelCertificateEndpoints
{
    /// <summary>
    /// Maps certificate inspection and export routes.
    /// </summary>
    public static IEndpointRouteBuilder MapPanelCertificates(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/panel/certificate").RequireScope(Scopes.ManageAccess);
        group.MapGet("/", (HttpContext context, PanelCertificates certificates, ILoggerFactory loggers) =>
            Answer(context, certificates, loggers, false, false));
        group.MapGet("/leaf.pem", (HttpContext context, PanelCertificates certificates, ILoggerFactory loggers) =>
            Answer(context, certificates, loggers, true, false));
        group.MapGet("/chain.pem", (HttpContext context, PanelCertificates certificates, ILoggerFactory loggers) =>
            Answer(context, certificates, loggers, true, true));
        return routes;
    }

    private static IResult Answer(HttpContext context, PanelCertificates certificates, ILoggerFactory loggers, bool download, bool chain)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (!download)
            {
                return Results.Ok(certificates.Read());
            }
            var pems = certificates.PublicChain();
            if (pems.Count == 0)
            {
                return Results.NotFound(new Failure("certificate-missing", "The panel has no configured TLS certificate."));
            }
            var text = string.Join('\n', chain ? pems : pems.Take(1)) + "\n";
            return Results.File(Encoding.ASCII.GetBytes(text), "application/x-pem-file",
                chain ? "panel-chain.pem" : "panel-certificate.pem");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or AsnContentException or ArgumentException)
        {
            loggers.CreateLogger(typeof(PanelCertificateEndpoints)).LogWarning(ex, "The panel certificate could not be read");
            return Results.Json(new Failure("certificate-unreadable", "The panel certificate could not be read."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
