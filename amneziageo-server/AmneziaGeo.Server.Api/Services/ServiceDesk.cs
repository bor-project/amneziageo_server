using System.Buffers;
using AmneziaGeo.Server.Core.Panel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Answers the services of an endpoint: the hello, the legs of a measurement, the subscriptions and the websocket of
/// the tunnel.
/// </summary>
public sealed class ServiceDesk
{
    /// <summary>
    /// The path the hello sits at.
    /// </summary>
    public const string HelloPath = "/api/hello";

    /// <summary>
    /// The path the legs of a measurement sit under.
    /// </summary>
    public const string SpeedPath = "/api/speed";

    private const int Chunk = 64 * 1024;

    private readonly IServiceScopeFactory _scopes;

    private readonly SpeedTickets _tickets;

    private readonly IReadOnlyList<IHelloFeature> _features;

    private readonly SubscriptionState _subscriptions;

    private readonly CancellationToken _stopping;

    private readonly JsonSerializerOptions _json;

    private readonly ILogger<ServiceDesk> _logger;

    private readonly RefusalHold _hold;

    private readonly AnswerSlots _slots;

    private readonly SubscriptionCertificates? _certificates;
    private readonly XHttpHost? _xhttp;
    private readonly ProxyHost? _fronts;

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceDesk(
        IServiceScopeFactory scopes,
        SpeedTickets tickets,
        IEnumerable<IHelloFeature> features,
        SubscriptionState subscriptions,
        IHostApplicationLifetime life,
        IOptions<JsonOptions> json,
        ILogger<ServiceDesk> logger,
        RefusalHold? hold = null,
        AnswerSlots? slots = null,
        SubscriptionCertificates? certificates = null,
        XHttpHost? xhttp = null,
        ProxyHost? fronts = null)
    {
        ArgumentNullException.ThrowIfNull(life);
        ArgumentNullException.ThrowIfNull(json);

        _scopes = scopes;
        _tickets = tickets;
        _features = [.. features];
        _subscriptions = subscriptions;
        _stopping = life.ApplicationStopping;
        _json = json.Value.SerializerOptions;
        _logger = logger;
        _hold = hold ?? new RefusalHold();
        _slots = slots ?? new AnswerSlots(0);
        _certificates = certificates;
        _xhttp = xhttp;
        _fronts = fronts;
    }

    /// <summary>
    /// Answers one request that reached the services of a port.
    /// </summary>
    public async Task AnswerAsync(HttpContext context, ServicePoint point)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(point);

        var route = Route(context, point);
        var proxyRoute = point.Endpoints.FirstOrDefault(endpoint => endpoint.ProxyId > 0
            && (route == Wanted.XHttp && endpoint.XHttp || route == Wanted.Front && endpoint.WebSocket || route == Wanted.Probe && endpoint.WebSocket)
            && context.Request.Path.StartsWithSegments("/" + endpoint.Path, StringComparison.Ordinal));
        if (route is Wanted.XHttp or Wanted.Front or Wanted.Probe
            && (context.Features.Get<ITlsConnectionFeature>() is null || proxyRoute is { UseCertificate: false } or { OriginHttp: true }))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        switch (route)
        {
            case Wanted.XHttp:
                await XHttpAsync(context, point).ConfigureAwait(false);
                break;
            case Wanted.Probe:
                await ProbeAsync(context, point).ConfigureAwait(false);
                break;
            case Wanted.Front:
                await PassAsync(context, point).ConfigureAwait(false);
                break;
            case Wanted.Hello:
                await _slots.AnswerAsync(() => HelloAsync(context, point), context.RequestAborted).ConfigureAwait(false);
                break;
            case Wanted.Down:
                await DownAsync(context).ConfigureAwait(false);
                break;
            case Wanted.Up:
                await UpAsync(context).ConfigureAwait(false);
                break;
            case Wanted.Subscription:
                await _slots.AnswerAsync(() => SubscriptionAnswer.WriteAsync(context, _subscriptions.Current, _scopes), context.RequestAborted)
                    .ConfigureAwait(false);
                break;
            default:
                if (Upgrades(context))
                {
                    await HoldAsync(context, Refuse(context, point.Names, "no websocket comes under that path")).ConfigureAwait(false);
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;
        }
    }

    /// <summary>
    /// Tells whether a request belongs to the services of a port.
    /// </summary>
    public bool Takes(HttpContext context, ServicePoint point)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(point);

        return Route(context, point) != Wanted.None;
    }

    /// <summary>
    /// Tells whether a request asks a port for a websocket none of its endpoints takes.
    /// </summary>
    public bool Strays(HttpContext context, ServicePoint point)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(point);

        return Upgrades(context) && Route(context, point) == Wanted.None;
    }

    private async Task HelloAsync(HttpContext context, ServicePoint point)
    {
        var ct = context.RequestAborted;
        var request = await ReadAsync(context).ConfigureAwait(false);
        if (request is null)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, new HelloFailure("bad-request", "the body is not a hello request"))
                .ConfigureAwait(false);

            return;
        }

        var proven = await ProveAsync(request, point, ct).ConfigureAwait(false);
        if (proven.Failure is { } failure)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, failure).ConfigureAwait(false);

            return;
        }

        var client = await HeardAsync(request, proven, ct).ConfigureAwait(false);
        var peer = new HelloPeer(client, proven.Endpoint!, proven.Template, context);
        var offered = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var feature in _features)
        {
            if (await feature.OfferAsync(peer, ct).ConfigureAwait(false) is { } arguments)
            {
                offered[feature.Name] = arguments;
            }
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new FeatureResponse(FeatureNames.Server, Version(), peer.Client.Name, offered),
            _json);
        context.Response.Headers.CacheControl = "no-store";
        await WriteAsync(context, StatusCodes.Status200OK, PeerToken.Seal(proven.Shared, Trim(request.Nonce), body))
            .ConfigureAwait(false);
    }

    // Keeps whether the application of a client takes the signal to disconnect, as its hello says.
    private async Task<TunnelClient> HeardAsync(HelloRequest request, Proven proven, CancellationToken ct)
    {
        var client = proven.Client!;
        var refused = Says(request, proven.Shared) is { Disconnect: false };
        if (client.DisconnectRefused == refused)
        {
            return client;
        }

        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ClientStore>().NoteRefusalAsync(client.Id, refused, ct).ConfigureAwait(false);
        if (refused)
        {
            _logger.LogInformation("the application of {Client} says it takes no signal to disconnect", client.Name);
        }
        else
        {
            _logger.LogInformation("the application of {Client} says it takes the signal to disconnect again", client.Name);
        }

        return client with { DisconnectRefused = refused };
    }

    // Opens what a hello says about its application; null when it says nothing the keys of the client hold.
    private Said? Says(HelloRequest request, byte[] shared)
    {
        if (request.Says is not { Iv: not null, Data: not null } words
            || PeerToken.Open(shared, Trim(request.Nonce), words, PeerToken.SaysContext) is not { } body)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Said>(body, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Hands a websocket to the front of the endpoint its path names; a path two endpoints share lets nothing in.
    private async Task PassAsync(HttpContext context, ServicePoint point)
    {
        var named = point.Endpoints.Where(one => one.WebSocket && Under(context.Request.Path, one.Path)).ToList();
        var modern = context.Request.Headers.ContainsKey("X-AmneziaGeo-Target");
        named = named.Where(endpoint => modern ? endpoint.ProxyId > 0 : endpoint.ProxyId == 0).ToList();
        if (named.Count != 1)
        {
            await HoldAsync(context, Refuse(context, string.Join(", ", named.Select(one => one.Name)), "shared-path")).ConfigureAwait(false);
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        var lease = default(ProxyLease?);
        if (named[0].ProxyId > 0 && _fronts is not null)
        {
            _fronts.TryLease(named[0].Name, out lease);
        }

        if (named[0].ProxyId == 0 && named[0].PolicyProxyId > 0 && _fronts is not null)
        {
            _fronts.TryLegacyLease(named[0].PolicyProxyId, out lease);
        }

        using var held = lease;
        using var lifetime = held is not null ? CancellationTokenSource.CreateLinkedTokenSource(_stopping, held.Stopped) : null;
        var stopped = lifetime?.Token ?? _stopping;
        if (named[0].ProxyId > 0)
        {
            var proxy = await AuthorizedProxyAsync(context, named[0]).ConfigureAwait(false);
            if (proxy is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var targets = ProxyTargetRules.Read(proxy.TargetsJson);
            if (targets is { Restricted: true })
            {
                if (!ProxyWsTarget.TryRead(context.Request.Headers.SecWebSocketProtocol.ToString(), out var target))
                {
                    await XHttpProbe.RefuseAsync(context, 400, "bad-proxy-target").ConfigureAwait(false);
                    return;
                }

                if (!ProxyTargetRules.Allows(targets, target!.Host, target.Port))
                {
                    await XHttpProbe.RefuseAsync(context, 403, "proxy-target-denied").ConfigureAwait(false);
                    return;
                }
            }
        }

        if (named[0].ProxyId == 0 && named[0].PolicyProxyId > 0)
        {
            using var scope = _scopes.CreateScope();
            var proxy = await scope.ServiceProvider.GetRequiredService<TransportProxyStore>()
                .FindAsync(named[0].PolicyProxyId, context.RequestAborted).ConfigureAwait(false);
            if (proxy is not { IsEnabled: true, UsesTls: true } || !Usable(scope.ServiceProvider, proxy) || proxy.LegacyConfigId != named[0].ConfigId
                || !ProxyTargetRules.Allows(ProxyTargetRules.Read(proxy.TargetsJson), ProxyDefaults.Loopback, named[0].Target))
            {
                await XHttpProbe.RefuseAsync(context, 403, "proxy-target-denied").ConfigureAwait(false);
                return;
            }
        }

        if ((named[0].ProxyId > 0 || named[0].PolicyProxyId > 0) && held is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (stopped.IsCancellationRequested)
        {
            context.Abort();
            return;
        }

        var refused = await FrontRelay.PassAsync(context, named[0].Front, stopped).ConfigureAwait(false);
        if (stopped.IsCancellationRequested)
        {
            context.Abort();
            return;
        }

        if (refused is null)
        {
            return;
        }

        var noted = Refuse(context, named[0].Name, refused.Reason, refused.Down);
        if (refused.Down)
        {
            refused = await AgainAsync(context, named[0].Front, refused, noted, stopped).ConfigureAwait(false);
        }
        else
        {
            await HoldAsync(context, noted).ConfigureAwait(false);
        }

        if (refused is not null)
        {
            context.Response.StatusCode = refused.Status;
        }
    }

    private async Task XHttpAsync(HttpContext context, ServicePoint point)
    {
        var endpoints = point.Endpoints.Where(endpoint => endpoint.XHttp && Under(context.Request.Path, endpoint.Path)).ToList();
        if (endpoints.Count != 1 || context.Features.Get<ITlsConnectionFeature>() is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var endpoint = endpoints[0];
        if (!XHttpRelay.Accepts(context, endpoint.HttpMethod))
        {
            return;
        }

        if (_xhttp is null || !_xhttp.TryLease(endpoint.ProxyId, out var lease))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var held = lease;
        await XHttpRelay.PassAsync(context, endpoint.Front, _logger, held.Stopped, endpoint.HttpMethod).ConfigureAwait(false);
    }

    private async Task ProbeAsync(HttpContext context, ServicePoint point)
    {
        var endpoints = point.Endpoints.Where(endpoint => endpoint.ProxyId > 0 && endpoint.WebSocket
            && XHttpProbe.Nonce(context.Request.Path, endpoint.Path, out _)).ToArray();
        if (endpoints.Length != 1 || !XHttpProbe.Nonce(context.Request.Path, endpoints[0].Path, out var nonce))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await ProbeAsync(context, endpoints[0], nonce).ConfigureAwait(false);
    }

    private async Task ProbeAsync(HttpContext context, ServiceEndpoint endpoint, string nonce)
    {
        var proxy = default(TransportProxyEntity?);
        async Task<bool> Authorized()
        {
            proxy = await AuthorizedProxyAsync(context, endpoint).ConfigureAwait(false);
            return proxy is not null;
        }

        var lease = default(ProxyLease?);
        var running = endpoint.XHttp
            ? _xhttp is not null && _xhttp.TryLease(endpoint.ProxyId, out lease)
            : _fronts is not null && _fronts.TryLease(endpoint.Name, out lease)
                && (await _fronts.StateAsync(endpoint.Name, context.RequestAborted).ConfigureAwait(false)).IsRunning;
        using var held = lease;
        bool Current() => held is not null && !held.Stopped.IsCancellationRequested
            && (endpoint.XHttp ? _xhttp?.CanRelay(endpoint.ProxyId) == true : _fronts?.CanRelay(endpoint.Name) == true);
        await XHttpProbe.AnswerAsync(context, nonce, Authorized, running,
            () => ProxyTargetRules.Read(proxy?.TargetsJson), endpoint.XHttp ? "xhttp" : "websocket", Current).ConfigureAwait(false);
    }

    private async Task<TransportProxyEntity?> AuthorizedProxyAsync(HttpContext context, ServiceEndpoint endpoint)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<TransportProxyStore>();
        var proxy = await store.FindAsync(endpoint.ProxyId, context.RequestAborted).ConfigureAwait(false);
        if (proxy is not { IsEnabled: true, UsesTls: true } || !Usable(scope.ServiceProvider, proxy))
        {
            return null;
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
        var credentials = await store.CredentialsAsync(proxy, context.RequestAborted).ConfigureAwait(false);
        return credentials.Any(credential => CryptographicOperations.FixedTimeEquals(digest,
            SHA256.HashData(Encoding.UTF8.GetBytes(credential)))) ? proxy : null;
    }

    private static bool Usable(IServiceProvider services, TransportProxyEntity proxy)
    {
        var panel = services.GetService<PanelSettings>() ?? new PanelSettings();
        var options = services.GetService<WebOptions>() ?? new WebOptions();
        return AmneziaGeo.Server.Api.Proxies.ProxySecurity.Check(proxy, Listening.Chain(options, panel), Listening.Key(options, panel)) is null;
    }

    // Counts a websocket refused to an address and writes down the start of a row and the start of the holding.
    private RefusalNote Refuse(HttpContext context, string endpoint, string reason, bool down = false)
    {
        var address = context.Connection.RemoteIpAddress;
        var noted = _hold.Note(address, down);
        if (noted.First)
        {
            _logger.LogInformation("the websocket of {Endpoint} was refused to {Address}: {Reason}", endpoint, address, reason);
        }

        if (noted.Begins)
        {
            _logger.LogWarning(
                "{Address} keeps asking for the websocket of {Endpoint} and is refused ({Reason}), {Count} times in {Seconds} s; each answer to it now waits {Wait} s",
                address,
                endpoint,
                reason,
                noted.Count,
                (int)noted.Lasted.TotalSeconds,
                (int)noted.Wait.TotalSeconds);
        }

        return noted;
    }

    // Holds the answer to a refused websocket for the wait it was given.
    private async Task HoldAsync(HttpContext context, RefusalNote noted)
    {
        if (noted.Wait <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping);
            await _hold.WaitAsync(noted.Wait, ended.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _hold.Left(context.Connection.RemoteIpAddress);
        }
    }

    // Holds the answer to a websocket whose front is down and hands it to the front again at every step of the wait.
    private async Task<FrontRefusal?> AgainAsync(HttpContext context, int front, FrontRefusal refused, RefusalNote noted, CancellationToken stopping)
    {
        if (noted.Wait <= TimeSpan.Zero)
        {
            return refused;
        }

        var address = context.Connection.RemoteIpAddress;
        var left = false;
        void Leave()
        {
            if (!left)
            {
                left = true;
                _hold.Left(address);
            }
        }

        try
        {
            using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
            var last = refused;
            for (var waited = TimeSpan.Zero; waited < noted.Wait && last is { Down: true }; waited += RefusalHold.Step)
            {
                await _hold.WaitAsync(RefusalHold.Step, ended.Token).ConfigureAwait(false);
                last = await FrontRelay.PassAsync(context, front, stopping, Leave).ConfigureAwait(false);
            }

            return last;
        }
        catch (OperationCanceledException)
        {
            return refused;
        }
        finally
        {
            Leave();
        }
    }

    // Tells what the services of a port make of a request.
    private Wanted Route(HttpContext context, ServicePoint point)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;
        if (point.Endpoints.Any(one => one.WebSocket && Under(path, one.Path)) && Upgrades(context))
        {
            return Wanted.Front;
        }

        if (point.Endpoints.Any(one => one.XHttp && Under(path, one.Path)))
        {
            return Wanted.XHttp;
        }

        if (point.Endpoints.Any(one => one.ProxyId > 0 && one.WebSocket && XHttpProbe.Nonce(path, one.Path, out _)))
        {
            return Wanted.Probe;
        }

        if (path.Equals(HelloPath, StringComparison.Ordinal) && HttpMethods.IsPost(method))
        {
            return Wanted.Hello;
        }

        if (path.Equals(SpeedPath + "/down", StringComparison.Ordinal) && HttpMethods.IsGet(method))
        {
            return Wanted.Down;
        }

        if (path.Equals(SpeedPath + "/up", StringComparison.Ordinal) && HttpMethods.IsPost(method))
        {
            return Wanted.Up;
        }

        var subscriptions = _subscriptions.Current;
        var asked = SubscriptionAnswer.Asked(path, subscriptions)
            ?? SubscriptionAnswer.AskedHold(path, subscriptions)
            ?? SubscriptionAnswer.AskedConfig(path, subscriptions)?.Subscription;

        return subscriptions.IsEnabled
            && (!subscriptions.Separate || subscriptions.Port == point.Port)
            && asked is not null
            && _certificates?.OnRequest(context.Request.Host.Host).IsGood != false
            ? Wanted.Subscription
            : Wanted.None;
    }

    // Tells whether a request comes under the path of a websocket.
    private static bool Under(PathString path, string socket) =>
        path.StartsWithSegments("/" + socket, StringComparison.Ordinal);

    // Tells whether a request asks for its connection to be switched to another protocol.
    private static bool Upgrades(HttpContext context) =>
        context.Features.Get<IHttpUpgradeFeature>() is { IsUpgradableRequest: true };

    // Proves the token of a hello and takes its nonce, which holds once.
    private async Task<Proven> ProveAsync(HelloRequest request, ServicePoint point, CancellationToken ct)
    {
        var key = Trim(request.Key);
        var nonce = Trim(request.Nonce);
        if (!Curve25519.IsKey(key))
        {
            return Proven.No(new HelloFailure("bad-key", "the key is not a key of 32 bytes in base64"));
        }

        if (!PeerToken.IsNonce(nonce))
        {
            return Proven.No(new HelloFailure("bad-nonce", "the nonce is not 16 bytes in base64"));
        }

        var now = _tickets.Now;
        if (!PeerToken.Fresh(request.Time, now))
        {
            return Proven.No(new HelloFailure("stale-time", "the token was counted too far from the clock of the server", now.ToUnixTimeSeconds()));
        }

        using var scope = _scopes.CreateScope();
        var client = await scope.ServiceProvider.GetRequiredService<ClientStore>().FindByKeyAsync(key, ct).ConfigureAwait(false);
        if (client is null || !client.IsEnabled || point.Of(client.ConfigId) is null)
        {
            return Proven.No(new HelloFailure("unknown-peer", "the endpoint carries no such peer"));
        }

        var endpoint = await scope.ServiceProvider.GetRequiredService<ConfigStore>().FindAsync(client.ConfigId, ct).ConfigureAwait(false);
        if (endpoint is null || !endpoint.IsEnabled)
        {
            return Proven.No(new HelloFailure("unknown-peer", "the endpoint of that peer is not up"));
        }

        var shared = PeerToken.Shared(endpoint.PrivateKey, client.PublicKey);
        if (!PeerToken.Holds(shared, key, request.Time, nonce, Trim(request.Proof)))
        {
            return Proven.No(new HelloFailure("bad-proof", "the token does not come from the key of that peer"));
        }

        if (!_tickets.Takes(nonce))
        {
            return Proven.No(new HelloFailure("replayed", "that token was already taken"));
        }

        var template = client.TemplateId is { } id
            ? await scope.ServiceProvider.GetRequiredService<TemplateStore>().FindAsync(id, ct).ConfigureAwait(false)
            : null;

        return new Proven(client, endpoint, template, shared, null);
    }

    private async Task DownAsync(HttpContext context)
    {
        if (_tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, new HelloFailure("unknown-ticket", "that pass is not one of ours"))
                .ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, new HelloFailure("measuring", "that pass is already measuring"))
                .ConfigureAwait(false);

            return;
        }

        try
        {
            var asked = long.TryParse(context.Request.Query["bytes"], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? count
                : SpeedTickets.DefaultBytes;
            var size = Math.Clamp(asked, 1, SpeedTickets.MaxBytes);
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = size;
            context.Response.Headers.CacheControl = "no-store";

            var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
            try
            {
                Array.Clear(buffer);
                for (var left = size; left > 0 && !context.RequestAborted.IsCancellationRequested;)
                {
                    var step = (int)Math.Min(left, Chunk);
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, step), context.RequestAborted).ConfigureAwait(false);
                    left -= step;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
        finally
        {
            _tickets.Leave(ticket.Value);
        }
    }

    private async Task UpAsync(HttpContext context)
    {
        if (_tickets.Find(context.Request.Query["ticket"]) is not { } ticket)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, new HelloFailure("unknown-ticket", "that pass is not one of ours"))
                .ConfigureAwait(false);

            return;
        }

        if (!_tickets.Enter(ticket.Value))
        {
            await RefuseAsync(context, StatusCodes.Status429TooManyRequests, new HelloFailure("measuring", "that pass is already measuring"))
                .ConfigureAwait(false);

            return;
        }

        var taken = 0L;
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            while (taken < SpeedTickets.MaxBytes)
            {
                var step = await context.Request.Body
                    .ReadAsync(buffer.AsMemory(0, Chunk), context.RequestAborted)
                    .ConfigureAwait(false);
                if (step == 0)
                {
                    break;
                }

                taken += step;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _tickets.Leave(ticket.Value);
        }

        context.Response.Headers.CacheControl = "no-store";
        await WriteAsync(context, StatusCodes.Status200OK, new { bytes = taken }).ConfigureAwait(false);
    }

    private async Task<HelloRequest?> ReadAsync(HttpContext context)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<HelloRequest>(context.Request.Body, _json, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task RefuseAsync(HttpContext context, int status, HelloFailure failure) =>
        WriteAsync(context, status, failure);

    private async Task WriteAsync<T>(HttpContext context, int status, T value)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(value, _json, context.RequestAborted).ConfigureAwait(false);
    }

    private static string Version() =>
        typeof(ServiceDesk).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string Trim(string? value) => value is null ? string.Empty : value.Trim();

    // What the services of a port make of a request.
    private enum Wanted
    {
        Probe = 7,
        None = 0,
        XHttp = 6,
        Front = 1,
        Hello = 2,
        Down = 3,
        Up = 4,
        Subscription = 5,
    }

    // What an application says about itself with its hello.
    private sealed record Said(bool? Disconnect);

    // The client behind a token and what it shares with its endpoint, or why the token was refused.
    private sealed record Proven(
        TunnelClient? Client,
        ServerConfig? Endpoint,
        ClientTemplate? Template,
        byte[] Shared,
        HelloFailure? Failure)
    {
        public static Proven No(HelloFailure failure) => new(null, null, null, [], failure);
    }
}
