using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Proxy;
using AmneziaGeo.Server.Routing.Proxy;
using AmneziaGeo.Server.Routing.XHttp;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AmneziaGeo.Server.Api.Proxies;

/// <summary>
/// Serves a proxy catalog without VPN interfaces.
/// </summary>
public sealed class StandaloneProxyService : IAsyncDisposable
{
    private readonly string _state;
    private readonly ILogger _logger;
    private readonly XHttpHost _http;
    private readonly ChildProxies _websockets;
    private readonly QuicHost _quic;
    private readonly Dictionary<long, int> _ports = [];
    private readonly List<WebApplication> _fronts = [];
    private readonly Dictionary<long, ProxyServiceEntry> _sockets = [];
    private string _configuration = "";
    private string _failure = "";
    private CancellationTokenSource _requests = new();
    private ProxyCatalog _catalog = new(1, []);
    private IReadOnlyDictionary<long, string> _blocked = new Dictionary<long, string>();

    /// <summary>
    /// ctor
    /// </summary>
    public StandaloneProxyService(string state, ILogger logger)
    {
        _state = state;
        _logger = logger;
        _http = new XHttpHost(Path.Combine(state, "http"), Path.Combine(AppContext.BaseDirectory, "wstunnel-get"), new ForwardLogger<XHttpHost>(logger));
        _websockets = new ChildProxies(Path.Combine(state, "ws"), Path.Combine(AppContext.BaseDirectory, "wstunnel"));
        _quic = new QuicHost(Path.Combine(state, "quic"), Path.Combine(AppContext.BaseDirectory, "wstunnel-quic"), logger);
    }

    public async Task WatchAsync(string file, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var candidate = "";
            try
            {
                var text = File.Exists(file) ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false) : "";
                candidate = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                if (text.Length > 4_194_304)
                {
                    throw new InvalidDataException("The proxy catalog is too large.");
                }

                var catalog = text.Length == 0 ? new ProxyCatalog(1, []) : JsonSerializer.Deserialize<ProxyCatalog>(text, ProxyCatalog.Json)
                    ?? throw new InvalidDataException("The proxy catalog is empty.");
                var (ready, blocked) = ProxySecurity.Active(catalog, text != _configuration ? _logger : null);
                if (JsonSerializer.Serialize(ready, ProxyCatalog.Json) != JsonSerializer.Serialize(_catalog, ProxyCatalog.Json))
                {
                    await ChangeAsync(ready, ct).ConfigureAwait(false);
                }

                _blocked = blocked;
                _configuration = text;
                _failure = "";

                var faults = await RefreshAsync(ct).ConfigureAwait(false);
                await ProxyCatalog.WriteAsync(Path.Combine(_state, "status.json"), new
                {
                    version = 1, updated = DateTimeOffset.UtcNow,
                    proxies = _catalog.Proxies.Select(one => new { one.Id, running = !faults.ContainsKey(one.Id), fault = faults.GetValueOrDefault(one.Id) ?? "" })
                        .Concat(_blocked.Select(one => new { Id = one.Key, running = false, fault = one.Value })),
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or CryptographicException)
            {
                if (_failure != candidate)
                {
                    _logger.LogWarning(ex, "The standalone proxy catalog could not be applied");
                }

                _failure = candidate;
                await ChangeAsync(new ProxyCatalog(1, []), CancellationToken.None).ConfigureAwait(false);
                _configuration = "";
                await ProxyCatalog.WriteAsync(Path.Combine(_state, "status.json"), new
                {
                    version = 1, updated = DateTimeOffset.UtcNow, fault = "invalid-proxy-catalog", proxies = Array.Empty<object>(),
                }, CancellationToken.None).ConfigureAwait(false);
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    private async Task ChangeAsync(ProxyCatalog catalog, CancellationToken ct)
    {
        await _requests.CancelAsync().ConfigureAwait(false);
        await _http.ApplyAsync([], ct).ConfigureAwait(false);
        await _quic.ApplyAsync([], ct).ConfigureAwait(false);
        foreach (var id in _sockets.Keys.ToArray())
        {
            await _websockets.StopAsync("standalone-" + id, ct).ConfigureAwait(false);
        }

        await StopFrontsAsync().ConfigureAwait(false);
        _requests.Dispose();
        _requests = new CancellationTokenSource();
        _sockets.Clear();
        _catalog = catalog;
        foreach (var proxy in catalog.Proxies.Where(one => one.Transport == "websocket"))
        {
            var name = "standalone-" + proxy.Id;
            var directory = Path.Combine(_state, "ws");
            if (OperatingSystem.IsWindows())
                    {
                        Directory.CreateDirectory(directory);
                    }
                    else
                    {
                        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
            var rules = Path.Combine(directory, ProxyFile.Rules(name));
            await File.WriteAllTextAsync(rules, ProxyFile.Whitelist(0, proxy.Path, proxy.Targets), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, ProxyFile.Arguments(name)), ProxyFile.Line(Port(proxy.Id), rules), ct).ConfigureAwait(false);
            await _websockets.StartAsync(name, ct).ConfigureAwait(false);
            _sockets[proxy.Id] = proxy;
        }

        foreach (var group in catalog.Proxies.Where(one => one.Transport != "quic").GroupBy(one => one.ListenPort == 0 ? one.Port : one.ListenPort))
        {
            var entries = group.ToArray();
            var certificates = entries.Where(one => one.UseCertificate).ToDictionary(one => one.Id, one => Certificate(one));
            foreach (var certificate in certificates.Values)
            {
                certificate.Current();
            }

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 4096;
                options.ListenAnyIP(group.Key, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1AndHttp2;
                    if (certificates.Count > 0)
                    {
                        var gate = new PlainGate(() => false);
                        listen.Use(next => context => gate.SortAsync(context, next));
                        listen.UseHttps(https => https.ServerCertificateSelector = (_, name) =>
                            entries.Where(one => certificates.ContainsKey(one.Id)).Select(one => (Entry: one, Certificate: certificates[one.Id]))
                                .FirstOrDefault(one => string.Equals(one.Entry.Host, name, StringComparison.OrdinalIgnoreCase)
                                    || !string.IsNullOrEmpty(name) && one.Certificate.Current().MatchesHostname(name)).Certificate?.Current() ?? certificates.Values.First().Current());
                        listen.Use(gate.Keep);
                    }
                });
            });
            var app = builder.Build();
            var stopping = _requests.Token;
            app.Run(context => AnswerAsync(context, entries, stopping));
            try
            {
                await app.StartAsync(ct).ConfigureAwait(false);
                _fronts.Add(app);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The standalone proxy listener did not bind");
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private static WebCertificate Certificate(ProxyServiceEntry proxy)
    {
        if (ProxySecurity.Check(ProxySecurity.Of(proxy)) is { } fault)
        {
            throw new InvalidDataException(fault.Code);
        }

        return new WebCertificate(proxy.Certificate, proxy.CertificateKey.Length == 0 ? proxy.Certificate : proxy.CertificateKey);
    }

    private async Task<Dictionary<long, string>> RefreshAsync(CancellationToken ct)
    {
        var faults = new Dictionary<long, string>();
        var http = _catalog.Proxies.Where(one => one.Transport is "http" or "xhttp").Select(one =>
            new XHttpProxy(one.Id, Port(one.Id), one.Path, one.Credentials, one.Targets, one.HttpMethod)).ToArray();
        foreach (var (id, fault) in await _http.ApplyAsync(http, ct).ConfigureAwait(false))
        {
            faults[id] = fault;
        }

        foreach (var (id, _) in _sockets)
        {
            var state = await _websockets.StateAsync("standalone-" + id, ct).ConfigureAwait(false);
            if (!state.IsRunning)
            {
                faults[id] = state.Message;
            }
        }

        foreach (var (id, fault) in await _quic.ApplyAsync(_catalog.Proxies.Where(one => one.Transport == "quic").ToArray(), ct).ConfigureAwait(false))
        {
            faults[id] = fault;
        }

        return faults;
    }

    private async Task AnswerAsync(HttpContext context, IReadOnlyList<ProxyServiceEntry> entries, CancellationToken stopping)
    {
        if (stopping.IsCancellationRequested)
        {
            context.Response.StatusCode = 503;
            return;
        }
        var proxy = entries.FirstOrDefault(one => context.Request.Path.StartsWithSegments("/" + one.Path, StringComparison.Ordinal));
        if (proxy is null || context.Features.Get<ITlsConnectionFeature>() is null || !proxy.UseCertificate || proxy.OriginHttp)
        {
            context.Response.StatusCode = 404;
            return;
        }

        if (proxy.Transport is "http" or "xhttp")
        {
            if (!_http.TryLease(proxy.Id, out var lease))
            {
                context.Response.StatusCode = 503;
                return;
            }

            using var held = lease;
            using var ended = CancellationTokenSource.CreateLinkedTokenSource(held.Stopped, stopping);
            await XHttpRelay.PassAsync(context, Port(proxy.Id), _logger, ended.Token, proxy.HttpMethod).ConfigureAwait(false);
            return;
        }

        var token = context.Request.Headers.Authorization.ToString();
        var allowed = proxy.Credentials.Any(credential => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes("Bearer " + credential), Encoding.UTF8.GetBytes(token)));
        if (!allowed)
        {
            context.Response.StatusCode = 403;
            return;
        }

        if (XHttpProbe.Nonce(context.Request.Path, proxy.Path, out var nonce))
        {
            var running = (await _websockets.StateAsync("standalone-" + proxy.Id, context.RequestAborted).ConfigureAwait(false)).IsRunning;
            await XHttpProbe.AnswerAsync(context, nonce, () => Task.FromResult(true), running, () => proxy.Targets, "websocket").ConfigureAwait(false);
            return;
        }

        if (proxy.Targets is { Restricted: true } && (!ProxyWsTarget.TryRead(context.Request.Headers.SecWebSocketProtocol.ToString(), out var target)
            || !ProxyTargetRules.Allows(proxy.Targets, target!.Host, target.Port)))
        {
            context.Response.StatusCode = 403;
            return;
        }

        var failure = await FrontRelay.PassAsync(context, Port(proxy.Id), stopping).ConfigureAwait(false);
        if (failure is not null && !context.Response.HasStarted)
        {
            context.Response.StatusCode = 502;
        }
    }

    private int Port(long id)
    {
        if (_ports.TryGetValue(id, out var held))
        {
            return held;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ports[id] = port;
        return port;
    }

    private async Task StopFrontsAsync()
    {
        foreach (var app in _fronts)
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }

        _fronts.Clear();

    }

    public async ValueTask DisposeAsync()
    {
        await _requests.CancelAsync().ConfigureAwait(false);
        _websockets.Dispose();
        await _http.DisposeAsync().ConfigureAwait(false);
        await _quic.DisposeAsync().ConfigureAwait(false);
        await StopFrontsAsync().ConfigureAwait(false);
        _requests.Dispose();
    }

    private sealed class ForwardLogger<T>(ILogger logger) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);
        public bool IsEnabled(LogLevel level) => logger.IsEnabled(level);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logger.Log(level, id, state, exception, formatter);
    }
}
