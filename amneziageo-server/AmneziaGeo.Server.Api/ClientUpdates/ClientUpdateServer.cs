using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Updates;
using Microsoft.AspNetCore.Connections;

namespace AmneziaGeo.Server.Api.ClientUpdates;

/// <summary>
/// Serves the releases of the client as the settings say: from a store of the panel, on the port of the panel or on
/// one of its own, or by pointing the clients at a service that stands apart.
/// </summary>
public sealed class ClientUpdateServer : IHostedService, IAsyncDisposable
{
    /// <summary>
    /// How often the service apart is asked whether it answers.
    /// </summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan StopFor = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan CheckFor = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopes;

    private readonly ClientUpdateState _state;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<ClientUpdateServer> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly CancellationTokenSource _stopping = new();

    private WebApplication? _app;

    private HttpClient? _remote;

    private Task? _watching;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientUpdateServer(
        IServiceScopeFactory scopes,
        ClientUpdateState state,
        PanelSettings panel,
        WebOptions options,
        TimeProvider time,
        ILogger<ClientUpdateServer> logger)
    {
        _scopes = scopes;
        _state = state;
        _panel = panel;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// The directory the store of the panel keeps the releases in when the settings name none.
    /// </summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(
            Path.GetDirectoryName(ServerDatabase.DefaultPath()) is { Length: > 0 } data ? data : "/var/lib/amneziageo-server",
            ClientUpdateDefaults.Folder);

    /// <summary>
    /// The releases as the panel reaches them now, null while they are off or their store does not take files.
    /// </summary>
    public IReleaseHub? Hub
    {
        get
        {
            var settings = _state.Current;
            if (!settings.IsEnabled)
            {
                return null;
            }

            if (settings.Own)
            {
                return _state.Store is { } store ? new LocalReleaseHub(store) : null;
            }

            return _remote is { } http ? new RemoteReleaseHub(http) : null;
        }
    }

    /// <summary>
    /// Serves the releases as the database holds their settings.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<ClientUpdateStore>().ReadAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.Current = settings;
            _state.Fault = await ServeAsync(settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        _watching = Task.Run(() => WatchAsync(_stopping.Token), CancellationToken.None);
    }

    /// <summary>
    /// Stops serving the releases, though the time to stop ran out.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
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
    /// Serves the releases as the settings say and returns why the host refused, keeping the ones before then.
    /// </summary>
    public async Task<string> ApplyAsync(ClientUpdateSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = _state.Current;
            _state.Current = settings;
            if (!settings.Rebinds(before) && _state.Fault.Length == 0)
            {
                return string.Empty;
            }

            var fault = await ServeAsync(settings).ConfigureAwait(false);
            if (fault.Length == 0)
            {
                _state.Fault = string.Empty;

                return string.Empty;
            }

            _state.Current = before;
            _state.Fault = await ServeAsync(before).ConfigureAwait(false);

            return fault;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Asks the service apart whether it answers and what it holds, null when it did not answer or there is none.
    /// </summary>
    public async Task<StoreStatus?> CheckAsync(CancellationToken ct)
    {
        if (_remote is not { } http)
        {
            return null;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(CheckFor);
            var status = await new RemoteReleaseHub(http).StatusAsync(limit.Token).ConfigureAwait(false);
            _state.AddressFault = string.Empty;
            _state.Checked = _time.GetUtcNow();

            return status;
        }
        catch (ReleaseRefused refused)
        {
            _state.AddressFault = refused.Message;
            _state.Checked = _time.GetUtcNow();

            return null;
        }
    }

    /// <summary>
    /// Returns the directory the store keeps the releases in under the settings.
    /// </summary>
    public static string DirectoryOf(ClientUpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Directory.Length > 0 ? settings.Directory : DefaultDirectory;
    }

    /// <summary>
    /// Stops serving the releases and lets the gate go.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await HaltAsync().ConfigureAwait(false);
        _stopping.Dispose();
        _gate.Dispose();
    }

    private async Task<string> ServeAsync(ClientUpdateSettings settings)
    {
        await HaltAsync().ConfigureAwait(false);
        _state.Store = null;
        _state.Detail = string.Empty;
        _state.AddressFault = string.Empty;
        _state.Pin = string.Empty;
        _state.Checked = null;
        if (!settings.IsEnabled)
        {
            return string.Empty;
        }

        if (!settings.Own)
        {
            _remote = Remote(settings);
            _ = Task.Run(() => CheckAsync(_stopping.Token), CancellationToken.None);

            return string.Empty;
        }

        var store = new ReleaseStore(DirectoryOf(settings), _time);
        var status = store.Status();
        if (!status.Writable)
        {
            _state.Detail = status.Fault;
            _logger.LogWarning("the releases of the client stay off: {Fault}", status.Fault);

            return "updates-directory";
        }

        _state.Store = store;
        if (settings.PortOr(_panel.Port) == _panel.Port)
        {
            _logger.LogInformation("the releases of the client answer under {Path} on the port of the panel", settings.Prefix);

            return string.Empty;
        }

        try
        {
            var app = Build(settings);
            try
            {
                await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _app = app;
            _logger.LogInformation("the releases of the client answer from *:{Port} under {Path}", settings.Port, settings.Prefix);

            return string.Empty;
        }
        catch (Exception ex)
            when (ex is IOException or SocketException or InvalidOperationException or CryptographicException)
        {
            _state.Store = null;
            _state.Detail = ex.Message;
            _logger.LogWarning(ex, "the releases of the client stayed off port {Port}", settings.Port);

            return ex is AddressInUseException || ex.InnerException is AddressInUseException ? "updates-port-busy" : "updates-failed";
        }
    }

    private WebApplication Build(ClientUpdateSettings settings)
    {
        var certificate = WebCertificate.Of(Listening.Chain(_options, _panel), Listening.Key(_options, _panel)) ?? WebCertificate.MadeUp();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, QuietLifetime>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = null;
            kestrel.ListenAnyIP(settings.Port, listen => Listening.Secure(listen, certificate));
        });

        var app = builder.Build();
        app.Run(async context =>
        {
            if (!await UpdateApi.TryAnswerAsync(context, settings.Prefix, _state.Store, Holds).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });

        return app;
    }

    // Tells whether a request carries the token of the store of the panel.
    private bool Holds(HttpContext context) => UpdateToken.Holds(context, _state.Current.Token);

    // Opens the way to the service apart: its certificate is taken as it comes and its digest kept for the clients.
    private HttpClient Remote(ClientUpdateSettings settings)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = CheckFor, UseProxy = false };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null)
            {
                return false;
            }

            _state.Pin = Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData()));

            return true;
        };

        var http = new HttpClient(handler) { BaseAddress = new Uri(settings.Address.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

        return http;
    }

    // Asks the service apart now and then while the releases come from it.
    private async Task WatchAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CheckEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await CheckAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The panel stops.
        }
    }

    private async Task HaltAsync()
    {
        _remote?.Dispose();
        _remote = null;
        var app = _app;
        if (app is null)
        {
            return;
        }

        _app = null;
        using var limit = new CancellationTokenSource(StopFor);
        await app.StopAsync(limit.Token).ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
    }
}
