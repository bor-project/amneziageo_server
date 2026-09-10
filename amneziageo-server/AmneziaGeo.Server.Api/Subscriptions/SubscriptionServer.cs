using System.Net.Sockets;
using System.Security.Cryptography;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Dal;
using Microsoft.AspNetCore.Connections;

namespace AmneziaGeo.Server.Api.Subscriptions;

/// <summary>
/// Serves the subscriptions on a port of their own.
/// </summary>
public sealed class SubscriptionServer : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan StopFor = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;

    private readonly SubscriptionState _state;

    private readonly PanelSettings _panel;

    private readonly WebOptions _options;

    private readonly ILogger<SubscriptionServer> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;

    /// <summary>
    /// ctor
    /// </summary>
    public SubscriptionServer(
        IServiceScopeFactory scopes,
        SubscriptionState state,
        PanelSettings panel,
        WebOptions options,
        ILogger<SubscriptionServer> logger)
    {
        _scopes = scopes;
        _state = state;
        _panel = panel;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Serves the subscriptions as the database holds them.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<SubscriptionStore>()
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

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
    }

    /// <summary>
    /// Stops serving the subscriptions.
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
    /// Serves the subscriptions as the settings say and returns why the host refused, keeping the ones before then.
    /// </summary>
    public async Task<string> ApplyAsync(SubscriptionSettings settings, CancellationToken ct)
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
    /// Stops serving the subscriptions and lets the gate go.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await HaltAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task<string> ServeAsync(SubscriptionSettings settings)
    {
        await HaltAsync().ConfigureAwait(false);
        if (!settings.IsEnabled || settings.Port == _panel.Port)
        {
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
            _logger.LogInformation(
                "the subscriptions answer from {Endpoints} under {Path}",
                string.Join(", ", settings.Entries),
                settings.Prefix);

            return string.Empty;
        }
        catch (Exception ex)
            when (ex is IOException or SocketException or InvalidOperationException or CryptographicException)
        {
            _logger.LogWarning(ex, "the subscriptions stayed off port {Port}", settings.Port);

            return Fault(ex);
        }
    }

    private WebApplication Build(SubscriptionSettings settings)
    {
        var certificate = WebCertificate.Of(Chain(settings), Key(settings));
        var plan = Listening.Plan(settings.Entries);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IHostLifetime, QuietLifetime>();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            foreach (var port in plan.AnyPorts)
            {
                kestrel.ListenAnyIP(port, listen => Listening.Secure(listen, certificate));
            }

            foreach (var point in plan.Points)
            {
                kestrel.Listen(point, listen => Listening.Secure(listen, certificate));
            }
        });

        var app = builder.Build();
        app.Run(context => SubscriptionAnswer.WriteAsync(context, _state.Current, _scopes));

        return app;
    }

    private string Chain(SubscriptionSettings settings) =>
        settings.Certificate.Length > 0 ? settings.Certificate : Listening.Chain(_options, _panel);

    private string Key(SubscriptionSettings settings) =>
        settings.Certificate.Length > 0 ? settings.CertificateKey : Listening.Key(_options, _panel);

    private async Task HaltAsync()
    {
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

    private static string Fault(Exception ex) =>
        ex is AddressInUseException || ex.InnerException is AddressInUseException
            ? "subscription-port-busy"
            : "subscription-failed";

    /// <summary>
    /// Leaves the signals of the process to the panel.
    /// </summary>
    private sealed class QuietLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
