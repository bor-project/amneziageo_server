using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Auth;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Maps transport proxy management routes.
/// </summary>
public static class ProxyEndpoints
{
    /// <summary>
    /// Registers the proxy routes.
    /// </summary>
    public static IEndpointRouteBuilder MapTransportProxies(this IEndpointRouteBuilder routes)
    {
        var reading = routes.MapGroup("/api/proxies").RequireScope(Scopes.ReadState);
        reading.MapGet("/", async (TransportProxyStore store, ServiceServer runtime, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(ct).ConfigureAwait(false)).Select(proxy =>
                ProxyResponse.Of(proxy) with { Fault = runtime.Faults.GetValueOrDefault(-proxy.Id) ?? string.Empty })));
        var writing = routes.MapGroup("/api/proxies").RequireScope(Scopes.ManageProxies);
        writing.MapGet("/draft", (PanelSettings panel) => Results.Ok(ProxyResponse.Of(new TransportProxyEntity
        {
            Port = panel.Port, Path = ConfigDefaults.WebSocketPath(), UseCertificate = true,
        })));
        writing.MapPost("/", (ProxyRequest draft, HttpContext context, CancellationToken ct) => SaveAsync(0, draft, context, ct));
        writing.MapPut("/{id:long}", SaveAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);
        writing.MapGet("/{id:long}/export", async (long id, TransportProxyStore store, HttpContext context, CancellationToken ct) =>
        {
            var proxy = await store.FindAsync(id, ct).ConfigureAwait(false);
            if (proxy is null)
            {
                return Results.NotFound();
            }

            if (proxy.IsHttp && !proxy.IsHttpV2)
            {
                return Refuse("proxy-protocol", "convert the legacy proxy before exporting it", 409);
            }

            var catalog = new ProxyCatalog(1, [new ProxyServiceEntry(proxy.Id, proxy.Name, proxy.IsHttpV2 ? "http" : proxy.Transport,
                proxy.Host, proxy.Port, proxy.Path, await store.CredentialsAsync(proxy, ct).ConfigureAwait(false), proxy.UsesCertificate,
                proxy.HttpMethod, proxy.ProtocolVersion, proxy.ListenPort, proxy.Certificate, proxy.CertificateKey, proxy.OriginHttp,
                ProxyTargetRules.Read(proxy.TargetsJson))]);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(catalog, ProxyCatalog.Json);
        });
        return routes;
    }

    private static async Task<IResult> SaveAsync(long id, ProxyRequest request, HttpContext context, CancellationToken ct)
    {
        var services = context.RequestServices;
        var store = services.GetRequiredService<TransportProxyStore>();
        var held = id == 0 ? null : await store.FindAsync(id, ct).ConfigureAwait(false);
        if (id != 0 && held is null)
        {
            return Refuse("unknown-proxy", "the proxy does not exist", 404);
        }

        var draft = request.Draft(held);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ProxyEndpoints));
        var fault = ProxyRules.Check(draft);
        if (fault is not null)
        {
            return Refuse(fault.Error, fault.Message);
        }

        if (draft.Hosted && draft.UsesCertificate && CertificateFiles.Check(draft.Certificate, draft.CertificateKey, logger) is { } certificate)
        {
            return Refuse(certificate.Code, certificate.Message);
        }

        if (await services.GetRequiredService<ProxyRules>().QuicConflictAsync(draft, id, ct).ConfigureAwait(false) is { } udp)
        {
            return Refuse("proxy-quic-port-taken", udp, 409);
        }

        var all = await store.ListAsync(ct).ConfigureAwait(false);
        if (all.Any(proxy => proxy.Id != id && string.Equals(proxy.Name, draft.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Refuse("name-taken", "a proxy already uses that name", 409);
        }

        if (await services.GetRequiredService<ProxyRules>().ConflictAsync(draft.ListenPort == 0 ? draft.Port : draft.ListenPort, draft.Path, id, held?.LegacyConfigId, ct).ConfigureAwait(false) is { } conflict)
        {
            return Results.Json(new { error = "proxy-path-taken", message = "the path is used by " + conflict, entity = conflict }, statusCode: 409);
        }

        var previous = request.Keep == true ? await services.GetRequiredService<FirewallApplier>().PlanAsync(ct).ConfigureAwait(false) : null;
        var saved = await store.SaveAsync(id, draft, ct).ConfigureAwait(false);
        await SettleAsync(services, ct, previous).ConfigureAwait(false);
        return Results.Ok(ProxyResponse.Of(saved) with
        {
            Fault = services.GetRequiredService<ServiceServer>().Faults.GetValueOrDefault(-saved.Id) ?? string.Empty,
        });
    }

    private static async Task<IResult> RemoveAsync(long id, HttpContext context, CancellationToken ct)
    {
        var store = context.RequestServices.GetRequiredService<TransportProxyStore>();
        if (await store.FindAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Refuse("unknown-proxy", "the proxy does not exist", 404);
        }

        await store.RemoveAsync(id, ct).ConfigureAwait(false);
        await SettleAsync(context.RequestServices, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task SettleAsync(IServiceProvider services, CancellationToken ct, FirewallPlan? previous = null)
    {
        var subscriptions = services.GetRequiredService<SubscriptionServer>();
        await subscriptions.ReleaseSharedAsync(ct).ConfigureAwait(false);
        var updates = services.GetRequiredService<ClientUpdateServer>();
        await updates.ReleaseSharedAsync(ct).ConfigureAwait(false);
        await services.GetRequiredService<ServiceServer>().SettleAsync(ct).ConfigureAwait(false);
        await subscriptions.ApplyAsync(services.GetRequiredService<SubscriptionState>().Current, ct).ConfigureAwait(false);
        await updates.ApplyAsync(services.GetRequiredService<ClientUpdateState>().Current, ct).ConfigureAwait(false);
        await services.GetRequiredService<FirewallApplier>().SettleAsync(previous, ct).ConfigureAwait(false);
    }

    private static IResult Refuse(string code, string message, int status = 400) => Results.Json(new Failure(code, message), statusCode: status);
}

/// <summary>
/// Carries proxy settings from the panel.
/// </summary>
public sealed record ProxyRequest(string Name, string Transport, string Host, int Port, string Path,
    bool IsEnabled, string Certificate = "", string CertificateKey = "", string ServerName = "", string Pin = "", bool? Opened = null, bool? Keep = null, int? ListenPort = null, bool? OriginHttp = null, ProxyTargets? Targets = null, bool? UseCertificate = null, string? HttpMethod = null, int? ProtocolVersion = null, bool? Hosted = null)
{
    /// <summary>
    /// Returns the proxy settings.
    /// </summary>
    public TransportProxyEntity Draft(TransportProxyEntity? held = null) => new()
    {
        Name = Name ?? "", Transport = Transport ?? "", Host = Host ?? "", Port = Port,
        ListenPort = ListenPort ?? held?.ListenPort ?? 0, OriginHttp = Transport is "http" or "xhttp" && (OriginHttp ?? held?.OriginHttp ?? false), Path = Path ?? "",
        Hosted = Hosted ?? held?.Hosted ?? true,
        UseCertificate = UseCertificate ?? held?.UseCertificate,
        HttpMethod = HttpMethod ?? held?.HttpMethod ?? "GET",
        ProtocolVersion = ProtocolVersion ?? (Transport == "http" ? 2 : held?.ProtocolVersion ?? 0),
        IsEnabled = IsEnabled, Opened = Opened ?? held?.Opened ?? false, Certificate = Certificate ?? "", CertificateKey = CertificateKey ?? "",
        ServerName = ServerName ?? "", Pin = Pin ?? "",
        TargetsJson = Targets is null ? held?.TargetsJson ?? string.Empty : ProxyTargetRules.Write(
            ProxyTargetRules.TryNormalize(Targets, out var normalized) ? normalized : Targets),
    };
}

/// <summary>
/// Describes a proxy without its transport secret.
/// </summary>
public sealed record ProxyResponse(long Id, string Name, string Transport, string Host, int Port, string Path,
    bool IsEnabled, string Certificate, string CertificateKey, string ServerName, string Pin, long? LegacyConfigId, bool Opened, string Fault = "", int ListenPort = 0, bool OriginHttp = false, ProxyTargets? Targets = null, bool? UseCertificate = null, string? HttpMethod = null, int? ProtocolVersion = null, bool? Hosted = null)
{
    /// <summary>
    /// Returns the public proxy settings.
    /// </summary>
    public static ProxyResponse Of(TransportProxyEntity proxy) => new(
        proxy.Id, proxy.Name, proxy.Transport, proxy.Host, proxy.Port, proxy.Path, proxy.IsEnabled,
        proxy.Certificate, proxy.CertificateKey, proxy.ServerName, proxy.Pin, proxy.LegacyConfigId, proxy.Opened, "", proxy.ListenPort, proxy.OriginHttp, ProxyTargetRules.Read(proxy.TargetsJson), proxy.UseCertificate, proxy.HttpMethod, proxy.ProtocolVersion, proxy.Hosted);
}
