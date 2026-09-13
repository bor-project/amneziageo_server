using System.Net;
using System.Net.Sockets;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace AmneziaGeo.Server.Api.Hello;

/// <summary>
/// Where the point of the server answers inside the tunnels.
/// </summary>
public sealed class HelloOptions
{
    /// <summary>
    /// The section of the configuration the options are read from.
    /// </summary>
    public const string Section = "Hello";

    /// <summary>
    /// The tcp port the point answers on, zero for the port of each interface.
    /// </summary>
    public int Port { get; set; }
}

/// <summary>
/// Lists the addresses the point of the server answers at.
/// </summary>
public static class HelloPoints
{
    /// <summary>
    /// Returns the address of every interface of the enabled endpoints with the port the point answers on there.
    /// </summary>
    public static IReadOnlyList<IPEndPoint> Of(IEnumerable<ServerConfig> configs, int configured)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var points = new List<IPEndPoint>();
        foreach (var config in configs.Where(one => one.IsEnabled))
        {
            var port = config.HelloPort(configured);
            if (port is <= 0 or > 65535)
            {
                continue;
            }

            foreach (var range in config.Address)
            {
                if (IPAddress.TryParse(range.Split('/')[0].Trim(), out var address)
                    && !points.Contains(new IPEndPoint(address, port)))
                {
                    points.Add(new IPEndPoint(address, port));
                }
            }
        }

        return points;
    }
}

/// <summary>
/// Serves the point of the server on the addresses of the interfaces.
/// </summary>
public sealed class HelloServer : IHostedService, IAsyncDisposable
{
    private const int SolIp = 0;

    private const int IpFreebind = 15;

    private const int SolIpv6 = 41;

    private const int Ipv6Freebind = 78;

    private static readonly TimeSpan StopFor = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;

    private readonly HelloOptions _options;

    private readonly HelloDesk _desk;

    private readonly ILogger<HelloServer> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;

    private IReadOnlyList<IPEndPoint> _points = [];

    /// <summary>
    /// ctor
    /// </summary>
    public HelloServer(IServiceScopeFactory scopes, HelloOptions options, HelloDesk desk, ILogger<HelloServer> logger)
    {
        _scopes = scopes;
        _options = options;
        _desk = desk;
        _logger = logger;
    }

    /// <summary>
    /// Serves the point on the interfaces the database holds.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => SettleAsync(cancellationToken);

    /// <summary>
    /// Stops serving the point.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await HaltAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Serves the point on the interfaces the database holds, binding again only when they changed.
    /// </summary>
    public async Task SettleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        var wanted = HelloPoints.Of(configs, _options.Port);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (wanted.SequenceEqual(_points) && (_app is not null || wanted.Count == 0))
            {
                return;
            }

            await HaltAsync().ConfigureAwait(false);
            var free = Free(wanted);
            if (free.Count == 0)
            {
                return;
            }

            var app = Build(free);
            try
            {
                await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                _logger.LogWarning(ex, "the point of the server stayed off {Points}", string.Join(", ", free));

                return;
            }

            _app = app;
            _points = wanted;
            _logger.LogInformation("the point of the server answers at {Points}", string.Join(", ", free));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops serving the point and lets the gate go.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await HaltAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private WebApplication Build(IReadOnlyList<IPEndPoint> points)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, QuietLifetime>();
        builder.WebHost.UseSockets(sockets => sockets.CreateBoundListenSocket = Bound);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            foreach (var point in points)
            {
                kestrel.Listen(point);
            }
        });

        var app = builder.Build();
        app.Run(_desk.AnswerAsync);

        return app;
    }

    // Returns the points no other socket of the host holds.
    private List<IPEndPoint> Free(IReadOnlyList<IPEndPoint> points)
    {
        var free = new List<IPEndPoint>();
        foreach (var point in points)
        {
            try
            {
                using var probe = Bound(point);
                free.Add(point);
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "the point of the server stayed off {Point}", point);
            }
        }

        return free;
    }

    // Binds a listen socket that takes an address before the interface carries it.
    private static Socket Bound(EndPoint endpoint)
    {
        if (endpoint is not IPEndPoint point || !OperatingSystem.IsLinux())
        {
            return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
        }

        var socket = new Socket(point.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            var six = point.AddressFamily == AddressFamily.InterNetworkV6;
            socket.SetRawSocketOption(six ? SolIpv6 : SolIp, six ? Ipv6Freebind : IpFreebind, BitConverter.GetBytes(1));
            socket.Bind(point);

            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task HaltAsync()
    {
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        _points = [];
        using var limit = new CancellationTokenSource(StopFor);
        await app.StopAsync(limit.Token).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }
}
