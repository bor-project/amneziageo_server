using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Where the services of one endpoint answer.
/// </summary>
/// <param name="ConfigId">The number of the endpoint.</param>
/// <param name="Name">The name of the interface of the endpoint.</param>
/// <param name="WebSocket">Whether the endpoint takes the tunnel inside a websocket.</param>
/// <param name="Front">The loopback port the websocket front of the endpoint listens on.</param>
/// <param name="Target">The UDP port the front hands the tunnel to.</param>
public sealed record ServiceEndpoint(long ConfigId, string Name, bool WebSocket, int Front, int Target);

/// <summary>
/// Where the services of the endpoints that share a TCP port answer.
/// </summary>
/// <param name="Port">The TCP port the services answer on.</param>
/// <param name="Endpoints">The endpoints the port serves.</param>
/// <param name="Chain">The certificate chain the port answers under, empty for one made up.</param>
/// <param name="Key">The key of the chain.</param>
public sealed record ServicePoint(int Port, IReadOnlyList<ServiceEndpoint> Endpoints, string Chain, string Key)
{
    /// <summary>
    /// Tells whether the port takes a tunnel inside a websocket.
    /// </summary>
    public bool WebSocket => Endpoints.Any(one => one.WebSocket);

    /// <summary>
    /// Names the endpoints the port serves.
    /// </summary>
    public string Names => string.Join(", ", Endpoints.Select(one => one.Name));

    /// <summary>
    /// Returns the endpoint the port serves under a number, null when it serves none.
    /// </summary>
    public ServiceEndpoint? Of(long configId) => Endpoints.FirstOrDefault(one => one.ConfigId == configId);

    /// <summary>
    /// Tells whether the port answers under another certificate than the other one.
    /// </summary>
    public bool Rebinds(ServicePoint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return !string.Equals(Chain, other.Chain, StringComparison.Ordinal)
            || !string.Equals(Key, other.Key, StringComparison.Ordinal);
    }
}

/// <summary>
/// The port of the services the panel answers on itself.
/// </summary>
public sealed class ServiceShare
{
    /// <summary>
    /// The port the panel shares with the services, null when it shares none.
    /// </summary>
    public ServicePoint? Point { get; set; }
}

/// <summary>
/// Lists where the services of the endpoints answer.
/// </summary>
public static class ServicePoints
{
    /// <summary>
    /// Returns the services of every endpoint that is turned on, the endpoints of one port together.
    /// </summary>
    public static IReadOnlyList<ServicePoint> Of(IEnumerable<ServerConfig> configs, string chain, string key)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var points = new List<ServicePoint>();
        foreach (var config in configs.Where(one => one.IsEnabled).OrderBy(one => one.Id))
        {
            var port = ConfigServices.Port(config);
            if (port is <= 0 or > 65535)
            {
                continue;
            }

            var endpoint = new ServiceEndpoint(config.Id, config.Name, config.WebSocket, ConfigServices.Front(config), config.ListenPort);
            var held = points.FindIndex(point => point.Port == port);
            if (held < 0)
            {
                points.Add(new ServicePoint(port, [endpoint], chain, key));

                continue;
            }

            points[held] = points[held] with { Endpoints = [.. points[held].Endpoints, endpoint] };
        }

        return points;
    }
}

/// <summary>
/// Serves the services of the endpoints on their TCP ports and keeps the websocket front of each on the loopback.
/// </summary>
public sealed class ServiceServer : IHostedService, IAsyncDisposable
{
    private const int BindTries = 10;

    private const string FrontDown = "the websocket front is down";

    private static readonly TimeSpan BindGap = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan StopFor = TimeSpan.FromSeconds(5);

    private static readonly WebCertificate Spare = WebCertificate.MadeUp();

    private readonly IServiceScopeFactory _scopes;

    private readonly ServiceDesk _desk;

    private readonly ProxyHost _fronts;

    private readonly WebOptions _options;

    private readonly PanelSettings _running;

    private readonly ServiceShare _share;

    private readonly ILogger<ServiceServer> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<int, Served> _served = [];

    private bool _sourcesForgotten;

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceServer(
        IServiceScopeFactory scopes,
        ServiceDesk desk,
        ProxyHost fronts,
        WebOptions options,
        PanelSettings running,
        ServiceShare share,
        ILogger<ServiceServer> logger)
    {
        _scopes = scopes;
        _desk = desk;
        _fronts = fronts;
        _options = options;
        _running = running;
        _share = share;
        _logger = logger;
    }

    /// <summary>
    /// Serves the services of the endpoints the database holds.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => SettleAsync(cancellationToken);

    /// <summary>
    /// Stops serving the services.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var port in _served.Keys.ToList())
            {
                await HaltAsync(port).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Serves the services of the endpoints the database holds, binding again only what changed, and returns why each
    /// websocket that stayed down did not come up.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> SettleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        var panel = await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(ct).ConfigureAwait(false);
        var wanted = ServicePoints.Of(configs, Listening.Chain(_options, panel), Listening.Key(_options, panel));

        var faults = new Dictionary<long, string>();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrontsAsync(wanted, faults, ct).ConfigureAwait(false);
            var mine = wanted.FirstOrDefault(point => point.Port == _running.Port);
            if (mine is not null && !string.Equals(_share.Point?.Names, mine.Names, StringComparison.Ordinal))
            {
                _logger.LogInformation("the services of {Name} answer on the port of the panel {Port}", mine.Names, mine.Port);
            }

            _share.Point = mine;

            var own = wanted.Where(point => point.Port != _running.Port).ToList();
            foreach (var port in _served.Keys.ToList())
            {
                var kept = own.Find(point => point.Port == port);
                if (kept is null || kept.Rebinds(_served[port].Point))
                {
                    await HaltAsync(port).ConfigureAwait(false);

                    continue;
                }

                _served[port].Point = kept;
            }

            foreach (var point in own.Where(one => !_served.ContainsKey(one.Port)))
            {
                var fault = await ServeAsync(point, ct).ConfigureAwait(false);
                if (fault.Length == 0)
                {
                    continue;
                }

                foreach (var endpoint in point.Endpoints.Where(one => one.WebSocket))
                {
                    faults.TryAdd(endpoint.ConfigId, fault);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return faults;
    }

    /// <summary>
    /// Stops serving the services and lets the gate go.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var port in _served.Keys.ToList())
        {
            await HaltAsync(port).ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    // Keeps a websocket front for every endpoint that takes one, noting the fronts that stayed down, and takes down
    // every other front of the host.
    private async Task FrontsAsync(IReadOnlyList<ServicePoint> wanted, Dictionary<long, string> faults, CancellationToken ct)
    {
        if (!_sourcesForgotten)
        {
            await _fronts.ForgetSourcesAsync(ct).ConfigureAwait(false);
            _sourcesForgotten = true;
        }

        var kept = wanted.SelectMany(point => point.Endpoints).Where(one => one.WebSocket).ToList();
        foreach (var name in _fronts.Held().Where(held => !kept.Exists(one => one.Name == held)))
        {
            var gone = await _fronts.WithdrawAsync(name, ct).ConfigureAwait(false);
            if (gone.Message.Length > 0)
            {
                _logger.LogWarning("the websocket front {Name} did not come down: {Reason}", name, gone.Message);
            }
        }

        foreach (var endpoint in kept)
        {
            var state = await _fronts.ApplyAsync(endpoint.Name, endpoint.Front, endpoint.Target, ct).ConfigureAwait(false);
            if (!state.IsRunning)
            {
                _logger.LogWarning("the websocket front of {Name} is down: {Reason}", endpoint.Name, state.Message);
                faults[endpoint.ConfigId] = state.Message.Length > 0 ? state.Message : FrontDown;
            }
        }
    }

    private async Task<string> ServeAsync(ServicePoint point, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var served = new Served(point);
            var app = Build(served);
            try
            {
                await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
                served.App = app;
                _served[point.Port] = served;
                _logger.LogInformation("the services of {Name} answer on TCP port {Port}", point.Names, point.Port);

                return string.Empty;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or CryptographicException)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                if (attempt >= BindTries || ex is CryptographicException)
                {
                    _logger.LogWarning(ex, "the services of {Name} stayed off TCP port {Port}", point.Names, point.Port);

                    return ex.Message;
                }
            }

            await Task.Delay(BindGap, ct).ConfigureAwait(false);
        }
    }

    private WebApplication Build(Served served)
    {
        var point = served.Point;
        var certificate = point.Chain.Length > 0
            ? new WebCertificate(point.Chain, point.Key.Length > 0 ? point.Key : point.Chain)
            : null;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, QuietLifetime>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = SpeedTickets.MaxBytes;
            kestrel.ListenAnyIP(point.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => Pick(certificate));
            });
        });

        var app = builder.Build();
        app.Run(context => _desk.AnswerAsync(context, served.Point));

        return app;
    }

    private async Task HaltAsync(int port)
    {
        if (!_served.Remove(port, out var served) || served.App is not { } app)
        {
            return;
        }

        using var limit = new CancellationTokenSource(StopFor);
        await app.StopAsync(limit.Token).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }

    // Returns the certificate of the panel, or the one made up when the panel holds none or its files do not read.
    private X509Certificate2 Pick(WebCertificate? certificate)
    {
        if (certificate is null)
        {
            return Spare.Current();
        }

        try
        {
            return certificate.Current();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning(ex, "the services answer under a made up certificate");

            return Spare.Current();
        }
    }

    // One TCP port of the services with the application that answers on it.
    private sealed class Served
    {
        /// <summary>
        /// ctor
        /// </summary>
        public Served(ServicePoint point)
        {
            Point = point;
        }

        public ServicePoint Point { get; set; }

        public WebApplication? App { get; set; }
    }
}
