using AmneziaGeo.Server.Api.Auth;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using AmneziaGeo.Server.Api.Firewall;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Api.ClientUpdates;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
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
        reading.MapGet("/udp", (HttpContext context) => Results.Ok(context.RequestServices.GetService<SharedUdpHost>()?.Snapshot() ?? []));
        reading.MapGet("/", async (TransportProxyStore store, ServiceServer runtime, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(ct).ConfigureAwait(false)).Select(proxy =>
                ProxyResponse.Of(proxy) with { Fault = runtime.Faults.GetValueOrDefault(-proxy.Id) ?? string.Empty })));
        var writing = routes.MapGroup("/api/proxies").RequireScope(Scopes.ManageProxies);
        writing.AddEndpointFilter<UdpChangesFilter>();
        writing.MapGet("/draft", (PanelSettings panel) => Results.Ok(ProxyResponse.Of(new TransportProxyEntity
        {
            Name = "proxy", Port = panel.Port, Path = ConfigDefaults.WebSocketPath(), UseCertificate = true,
            TargetsJson = ProxyTargetRules.Write(ProxyTargetRules.LocalOnly),
        })));
        writing.MapPost("/", (ProxyRequest draft, HttpContext context, CancellationToken ct) => SaveAsync(0, draft, context, ct));
        writing.MapPut("/{id:long}", SaveAsync);
        writing.MapDelete("/{id:long}", RemoveAsync);
        writing.MapGet("/{id:long}/tokens", async (long id, TransportProxyStore store, CancellationToken ct) =>
            await store.FindAsync(id, ct).ConfigureAwait(false) is { } proxy ? Results.Ok(store.Tokens(proxy)) : Results.NotFound());
        writing.MapPost("/{id:long}/tokens", async (long id, ProxyTokenRequest request, TransportProxyStore store, HttpContext context, CancellationToken ct) =>
        {
            var result = await store.MintAsync(id, request.Name ?? string.Empty, ct).ConfigureAwait(false);
            if (result.Minted is null)
            {
                return Refuse(result.Error, result.Error, result.Error == "unknown-proxy" ? 404 : result.Error == "bad-proxy-token-name" ? 400 : 409);
            }

            await SettleAsync(context.RequestServices, ct).ConfigureAwait(false);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Ok(result.Minted);
        });
        writing.MapDelete("/{id:long}/tokens/{tokenId:guid}", async (long id, Guid tokenId, TransportProxyStore store, HttpContext context, CancellationToken ct) =>
        {
            if (!await store.RevokeAsync(id, tokenId.ToString("D"), ct).ConfigureAwait(false))
            {
                return Results.NotFound();
            }

            await SettleAsync(context.RequestServices, ct).ConfigureAwait(false);
            return Results.NoContent();
        });
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

            var panel = context.RequestServices.GetRequiredService<PanelSettings>();
            var options = context.RequestServices.GetRequiredService<WebOptions>();
            if (ProxySecurity.Check(proxy, Listening.Chain(options, panel), Listening.Key(options, panel)) is { } fault)
            {
                return Refuse(fault.Code, fault.Message, 409);
            }

            var catalog = new ProxyCatalog(1, [new ProxyServiceEntry(proxy.Id, proxy.Name, proxy.IsHttpV2 ? "http" : proxy.Transport,
                proxy.Host, proxy.Port, proxy.Path, await store.CredentialsAsync(proxy, ct).ConfigureAwait(false), proxy.UsesCertificate,
                proxy.HttpMethod, proxy.ProtocolVersion, proxy.ListenPort,
                proxy.Certificate.Length > 0 ? proxy.Certificate : Listening.Chain(options, panel),
                proxy.CertificateKey.Length > 0 ? proxy.CertificateKey : proxy.Certificate.Length > 0 ? proxy.Certificate : Listening.Key(options, panel),
                false, await store.TargetsAsync(proxy, ct).ConfigureAwait(false), proxy.ServerName, proxy.Pin, proxy.AllowAnonymous, store.BearerIds(proxy))]);
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

        var panel = services.GetRequiredService<PanelSettings>();
        var options = services.GetRequiredService<WebOptions>();
        if (ProxySecurity.Check(draft, Listening.Chain(options, panel), Listening.Key(options, panel), logger) is { } certificate)
        {
            return Refuse(certificate.Code, certificate.Message);
        }

        if (await services.GetRequiredService<ProxyRules>().QuicConflictAsync(draft, id, ct, Listening.Chain(options, panel), Listening.Key(options, panel)).ConfigureAwait(false) is { } udp)
        {
            return Refuse("quic-port-requires-one-certificate", udp, 409);
        }

        var all = await store.ListAsync(ct).ConfigureAwait(false);
        if (all.Any(proxy => proxy.Id != id && (proxy.Transport == draft.Transport || proxy.IsHttp && draft.IsHttp)
            && string.Equals(proxy.Name, draft.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Refuse("name-taken", "a proxy already uses that name and transport", 409);
        }

        if (await services.GetRequiredService<ProxyRules>().ConflictAsync(draft.ListenPort == 0 ? draft.Port : draft.ListenPort, draft.Path, id, held?.LegacyConfigId, ct).ConfigureAwait(false) is { } conflict)
        {
            return Results.Json(new { error = "proxy-path-taken", message = "the path is used by " + conflict, entity = conflict, field = "path" }, statusCode: 409);
        }

        if (await services.GetRequiredService<ServiceServer>().CheckUdpAsync(ct, proxy: draft, proxyId: id).ConfigureAwait(false) is { } listener)
        {
            return Results.Json(new { error = listener.Error, message = listener.Message, field = "listenPort" }, statusCode: 409);
        }

        var previous = request.Keep == true ? await services.GetRequiredService<FirewallApplier>().PlanAsync(ct).ConfigureAwait(false) : null;
        var db = services.GetRequiredService<AppDbContext>();
        var legacy = held?.LegacyConfigId is { } configId
            ? await db.Configs.AsNoTracking().SingleOrDefaultAsync(one => one.Id == configId, ct).ConfigureAwait(false) : null;
        var saved = await store.SaveAsync(id, draft, ct).ConfigureAwait(false);
        var failure = string.Empty;
        try
        {
            await SettleAsync(services, ct, previous).ConfigureAwait(false);
            if (saved.IsEnabled && saved.Hosted)
            {
                failure = services.GetRequiredService<ServiceServer>().Faults.GetValueOrDefault(-saved.Id) ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning(ex, "The proxy listener could not be applied");
            failure = ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                ? "udp-port-held-by-another-process" : "proxy-listener-unavailable";
        }

        if (failure.Length > 0)
        {
            await store.RestoreAsync(saved.Id, held, legacy?.WebSocket, legacy?.UpdatedUtc, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await SettleAsync(services, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The previous proxy listeners could not be restored");
                return Refuse("proxy-restore-failed", "the previous settings were restored but their listeners require attention", 503);
            }

            ct.ThrowIfCancellationRequested();
            return Results.Json(new { error = "proxy-listener-unavailable", field = "listenPort", reason = failure,
                message = "the proxy listener could not start; the previous settings were restored" }, statusCode: 409);
        }

        return Results.Ok(ProxyResponse.Of(saved));
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

    private static IResult Refuse(string code, string message, int status = 400) => Results.Json(new { error = code, message, field = ProxyRules.Field(code) }, statusCode: status);
}

/// <summary>
/// Carries proxy settings from the panel.
/// </summary>
public sealed record ProxyRequest(string Name, string Transport, string Host, int Port, string Path,
    bool IsEnabled, string? Certificate = null, string? CertificateKey = null, string? ServerName = null, string? Pin = null, bool? Opened = null, bool? Keep = null, int? ListenPort = null, bool? OriginHttp = null, ProxyTargets? Targets = null, bool? UseCertificate = null, string? HttpMethod = null, int? ProtocolVersion = null, bool? Hosted = null, bool? AllowAnonymous = null)
{
    /// <summary>
    /// Returns the proxy settings.
    /// </summary>
    public TransportProxyEntity Draft(TransportProxyEntity? held = null) => new()
    {
        Name = (Name ?? "").Trim(), Transport = Transport ?? "", Host = (Host ?? "").Trim(), Port = Port,
        ListenPort = ListenPort ?? held?.ListenPort ?? 0, OriginHttp = OriginHttp ?? held?.OriginHttp ?? false, Path = Path ?? "",
        Hosted = Hosted ?? held?.Hosted ?? true,
        AllowAnonymous = AllowAnonymous ?? held?.AllowAnonymous ?? false,
        UseCertificate = UseCertificate ?? held?.UseCertificate,
        HttpMethod = HttpMethod ?? held?.HttpMethod ?? "GET",
        ProtocolVersion = ProtocolVersion ?? (Transport == "http" ? 2 : held?.ProtocolVersion ?? 0),
        IsEnabled = IsEnabled, Opened = Opened ?? held?.Opened ?? false, Certificate = Certificate ?? held?.Certificate ?? "", CertificateKey = CertificateKey ?? held?.CertificateKey ?? "",
        ServerName = (ServerName ?? held?.ServerName ?? "").Trim(), Pin = (Pin ?? held?.Pin ?? "").Trim(),
        TargetsJson = Targets is null ? held?.TargetsJson ?? ProxyTargetRules.Write(ProxyTargetRules.LocalOnly) : ProxyTargetRules.Write(
            ProxyTargetRules.TryNormalize(Targets, out var normalized) ? normalized : Targets),
    };
}

/// <summary>
/// Describes a proxy without its transport secret.
/// </summary>
public sealed record ProxyResponse(long Id, string Name, string Transport, string Host, int Port, string Path,
    bool IsEnabled, string Certificate, string CertificateKey, string ServerName, string Pin, long? LegacyConfigId, bool Opened, string Fault = "", int ListenPort = 0, bool OriginHttp = false, ProxyTargets? Targets = null, bool? UseCertificate = null, string? HttpMethod = null, int? ProtocolVersion = null, bool? Hosted = null, bool AllowAnonymous = false)
{
    /// <summary>
    /// Returns the public proxy settings.
    /// </summary>
    public static ProxyResponse Of(TransportProxyEntity proxy) => new(
        proxy.Id, proxy.Name, proxy.Transport, proxy.Host, proxy.Port, proxy.Path, proxy.IsEnabled,
        proxy.Certificate, proxy.CertificateKey, proxy.ServerName, proxy.Pin, proxy.LegacyConfigId, proxy.Opened, "", proxy.ListenPort, proxy.OriginHttp, ProxyTargetRules.Read(proxy.TargetsJson), proxy.UseCertificate, proxy.HttpMethod, proxy.ProtocolVersion, proxy.Hosted, proxy.AllowAnonymous);
}

public sealed record ProxyTokenRequest(string Name);
