using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Proxy;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AmneziaGeo.Server.Api.Services;

/// <summary>
/// Where the services of one endpoint answer.
/// </summary>
/// <param name="ConfigId">The number of the endpoint.</param>
/// <param name="Name">The name of the interface of the endpoint.</param>
/// <param name="Port">The TCP port the services answer on.</param>
/// <param name="WebSocket">Whether the port takes the tunnel inside a websocket.</param>
/// <param name="Front">The loopback port the websocket front of the endpoint listens on.</param>
/// <param name="Target">The UDP port the front hands the tunnel to.</param>
/// <param name="Chain">The certificate chain the port answers under, empty for one made up.</param>
/// <param name="Key">The key of the chain.</param>
public sealed record ServicePoint(long ConfigId, string Name, int Port, bool WebSocket, int Front, int Target, string Chain, string Key);

/// <summary>
/// Lists where the services of the endpoints answer.
/// </summary>
public static class ServicePoints
{
    /// <summary>
    /// Returns the services of every endpoint that is turned on, one port each.
    /// </summary>
    public static IReadOnlyList<ServicePoint> Of(IEnumerable<ServerConfig> configs, string chain, string key)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var points = new List<ServicePoint>();
        foreach (var config in configs.Where(one => one.IsEnabled).OrderBy(one => one.Id))
        {
            var port = ConfigServices.Port(config);
            if (port is <= 0 or > 65535 || points.Exists(point => point.Port == port))
            {
                continue;
            }

            points.Add(new ServicePoint(config.Id, config.Name, port, config.WebSocket, ConfigServices.Front(config), config.ListenPort, chain, key));
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

    private static readonly TimeSpan BindGap = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan StopFor = TimeSpan.FromSeconds(5);

    private static readonly Lazy<X509Certificate2> MadeUp = new(MakeUp);

    private readonly IServiceScopeFactory _scopes;

    private readonly ServiceDesk _desk;

    private readonly ProxyHost _fronts;

    private readonly WebOptions _options;

    private readonly ILogger<ServiceServer> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<int, Served> _served = [];

    private bool _sourcesForgotten;

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceServer(IServiceScopeFactory scopes, ServiceDesk desk, ProxyHost fronts, WebOptions options, ILogger<ServiceServer> logger)
    {
        _scopes = scopes;
        _desk = desk;
        _fronts = fronts;
        _options = options;
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
    /// Serves the services of the endpoints the database holds, binding again only what changed.
    /// </summary>
    public async Task SettleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        var panel = await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(ct).ConfigureAwait(false);
        var wanted = ServicePoints.Of(configs, Listening.Chain(_options, panel), Listening.Key(_options, panel));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrontsAsync(wanted, ct).ConfigureAwait(false);
            foreach (var port in _served.Keys.ToList())
            {
                if (!wanted.Contains(_served[port].Point))
                {
                    await HaltAsync(port).ConfigureAwait(false);
                }
            }

            foreach (var point in wanted.Where(one => !_served.ContainsKey(one.Port)))
            {
                await ServeAsync(point, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
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

    // Keeps a websocket front for every endpoint that takes one and takes down every other front of the host.
    private async Task FrontsAsync(IReadOnlyList<ServicePoint> wanted, CancellationToken ct)
    {
        if (!_sourcesForgotten)
        {
            await _fronts.ForgetSourcesAsync(ct).ConfigureAwait(false);
            _sourcesForgotten = true;
        }

        var kept = wanted.Where(point => point.WebSocket).ToList();
        foreach (var name in _fronts.Held().Where(held => !kept.Exists(point => point.Name == held)))
        {
            var gone = await _fronts.WithdrawAsync(name, ct).ConfigureAwait(false);
            if (gone.Message.Length > 0)
            {
                _logger.LogWarning("the websocket front {Name} did not come down: {Reason}", name, gone.Message);
            }
        }

        foreach (var point in kept)
        {
            var state = await _fronts.ApplyAsync(point.Name, point.Front, point.Target, ct).ConfigureAwait(false);
            if (!state.IsRunning)
            {
                _logger.LogWarning("the websocket front of {Name} is down: {Reason}", point.Name, state.Message);
            }
        }
    }

    private async Task ServeAsync(ServicePoint point, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var app = Build(point);
            try
            {
                await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
                _served[point.Port] = new Served(point, app);
                _logger.LogInformation("the services of {Name} answer on TCP port {Port}", point.Name, point.Port);

                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or CryptographicException)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                if (attempt >= BindTries || ex is CryptographicException)
                {
                    _logger.LogWarning(ex, "the services of {Name} stayed off TCP port {Port}", point.Name, point.Port);

                    return;
                }
            }

            await Task.Delay(BindGap, ct).ConfigureAwait(false);
        }
    }

    private WebApplication Build(ServicePoint point)
    {
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
        app.Run(context => _desk.AnswerAsync(context, point));

        return app;
    }

    private async Task HaltAsync(int port)
    {
        if (!_served.Remove(port, out var served))
        {
            return;
        }

        using var limit = new CancellationTokenSource(StopFor);
        await served.App.StopAsync(limit.Token).ConfigureAwait(false);
        await served.App.DisposeAsync().ConfigureAwait(false);
    }

    // Returns the certificate of the panel, or the one made up when the panel holds none or its files do not read.
    private X509Certificate2 Pick(WebCertificate? certificate)
    {
        if (certificate is null)
        {
            return MadeUp.Value;
        }

        try
        {
            return certificate.Current();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning(ex, "the services answer under a made up certificate");

            return MadeUp.Value;
        }
    }

    // Makes up the certificate a port answers under when the panel holds none.
    private static X509Certificate2 MakeUp()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var now = DateTimeOffset.UtcNow;
        using var made = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));

        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
    }

    private sealed record Served(ServicePoint Point, WebApplication App);
}
