using System.ComponentModel;
using System.Net.NetworkInformation;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Api.Web;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using AmneziaGeo.Server.Core.Status;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Dns;
using AmneziaGeo.Server.Routing.Firewall;
using AmneziaGeo.Server.Routing.Host;
using AmneziaGeo.Server.Routing.Proxy;

namespace AmneziaGeo.Server.Api.Status;

/// <summary>
/// Checks the services the server runs for its clients, keeps what it found and says in the log when one stops or
/// starts working again.
/// </summary>
public sealed class ServiceWatch : BackgroundService
{
    /// <summary>
    /// The file beside the database the menu of the host reads the line of the services from.
    /// </summary>
    public const string FileName = "services";

    /// <summary>
    /// How often the services are checked.
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan First = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan WallsFor = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;

    private readonly ServiceServer _services;

    private readonly ProxyHost _fronts;

    private readonly SubscriptionState _subscriptions;

    private readonly FirewallHost _firewall;

    private readonly DnsState _resolver;

    private readonly PanelSettings _panel;

    private readonly TimeProvider _time;

    private readonly ILogger<ServiceWatch> _logger;

    private readonly string _file;

    private readonly SystemProbe _probe = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, long> _falls = new(StringComparer.Ordinal);

    private volatile ServicesReport _report = ServicesReport.None;

    private IReadOnlyDictionary<ServicePort, string> _walls = new Dictionary<ServicePort, string>();

    private DateTimeOffset _wallsAt = DateTimeOffset.MinValue;

    private string _line = string.Empty;

    /// <summary>
    /// ctor
    /// </summary>
    public ServiceWatch(
        IServiceScopeFactory scopes,
        ServiceServer services,
        ProxyHost fronts,
        SubscriptionState subscriptions,
        FirewallHost firewall,
        DnsState resolver,
        PanelSettings panel,
        PanelHealth health,
        TimeProvider time,
        ILogger<ServiceWatch> logger)
    {
        ArgumentNullException.ThrowIfNull(health);

        _scopes = scopes;
        _services = services;
        _fronts = fronts;
        _subscriptions = subscriptions;
        _firewall = firewall;
        _resolver = resolver;
        _panel = panel;
        _time = time;
        _logger = logger;
        _file = Path.Combine(Path.GetDirectoryName(health.File) ?? ".", FileName);
    }

    /// <summary>
    /// The services as the last check found them.
    /// </summary>
    public ServicesReport Report => _report;

    /// <summary>
    /// Checks the services now and returns what it found.
    /// </summary>
    public async Task<ServicesReport> CheckAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var found = ServiceChecks.Of(await FactsAsync(now, ct).ConfigureAwait(false), now);
            var before = _report;
            var report = new ServicesReport(now, [.. found.Select(one => Carried(one, before))]);
            Say(report, before);
            _report = report;
            Write(report);

            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Lets the gate go.
    /// </summary>
    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(First, _time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Period, _time);
            do
            {
                try
                {
                    await CheckAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    _logger.LogWarning(ex, "the services were not checked");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Keeps the moment a service began or stopped working while it goes on as it was.
    private static ServiceHealth Carried(ServiceHealth found, ServicesReport before) =>
        before.Services.FirstOrDefault(one => Same(one, found)) is { } old && old.Works == found.Works
            ? found with { Since = old.Since }
            : found;

    private static bool Same(ServiceHealth one, ServiceHealth other) =>
        one.Kind == other.Kind && string.Equals(one.Name, other.Name, StringComparison.Ordinal);

    private static IReadOnlySet<string>? Links()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Select(one => one.Name).ToHashSet(StringComparer.Ordinal);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    // Reads the sockets of both families once, nothing when they do not read.
    private static string? Sockets()
    {
        try
        {
            return File.ReadAllText("/proc/net/tcp") + "\n" + File.ReadAllText("/proc/net/tcp6");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<ServiceFacts> FactsAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
        var subscription = _subscriptions.Current;
        var settings = _resolver.Settings;
        var resolver = new ResolverFacts(
            settings?.IsEnabled == true,
            _resolver.IsRunning,
            settings?.Port ?? 0,
            _resolver.Fault ?? string.Empty,
            _resolver.Way ?? string.Empty);

        return new ServiceFacts(
            configs,
            _panel.Port,
            subscription,
            _subscriptions.Fault,
            _services.Refused,
            await FrontsAsync(configs, ct).ConfigureAwait(false),
            _probe.Tunnel().Loaded,
            Links(),
            await WallsAsync(ServiceChecks.Exposed(configs, subscription), now, ct).ConfigureAwait(false),
            resolver);
    }

    // Asks the host about the front of every endpoint that takes a websocket and counts its falls since the check before.
    private async Task<IReadOnlyDictionary<string, FrontFacts>> FrontsAsync(IReadOnlyList<ServerConfig> configs, CancellationToken ct)
    {
        var fronts = new Dictionary<string, FrontFacts>(StringComparer.Ordinal);
        var watched = configs.Where(one => one.IsEnabled && one.WebSocket).ToList();
        var sockets = watched.Count > 0 ? Sockets() : null;
        foreach (var config in watched)
        {
            var state = await _fronts.StateAsync(config.Name, ct).ConfigureAwait(false);
            var fell = _falls.TryGetValue(config.Name, out var before) && state.Falls > before ? state.Falls - before : 0;
            _falls[config.Name] = state.Falls;
            var port = ConfigServices.Front(config);
            var listens = sockets is null || ProcText.Listens(sockets, port);
            fronts[config.Name] = new FrontFacts(state.IsRunning, state.Message, fell, listens);
        }

        foreach (var gone in _falls.Keys.Where(name => !fronts.ContainsKey(name)).ToList())
        {
            _falls.Remove(gone);
        }

        return fronts;
    }

    // Reads the firewall of the host at most once in five minutes, and at once when a port is new to it.
    private async Task<IReadOnlyDictionary<ServicePort, string>> WallsAsync(
        IReadOnlyList<ServicePort> ports,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (now - _wallsAt < WallsFor && ports.All(_walls.ContainsKey))
        {
            return _walls;
        }

        try
        {
            var states = await _firewall.StatesAsync([.. ports.Select(one => new FirewallPort(one.Protocol, one.Port, string.Empty))], ct)
                .ConfigureAwait(false);
            _walls = ports.Zip(states).ToDictionary(pair => pair.First, pair => pair.Second);
        }
        catch (Exception ex) when (ex is HostNetworkException or IOException or UnauthorizedAccessException
            or InvalidOperationException or Win32Exception)
        {
            _logger.LogWarning(ex, "the firewall of the host did not tell whether the ports of the services are open");
            _walls = new Dictionary<ServicePort, string>();
        }

        _wallsAt = now;

        return _walls;
    }

    // Writes a line to the log for each service that stopped working or works again.
    private void Say(ServicesReport report, ServicesReport before)
    {
        foreach (var service in report.Services)
        {
            var old = before.Services.FirstOrDefault(one => Same(one, service));
            if (!service.Works && old is not { Works: false })
            {
                _logger.LogWarning(
                    "the service {Service} is down: {Reason}",
                    ServiceChecks.Label(service),
                    ServiceChecks.Reason(service));
            }
            else if (service.Works && old is { Works: false })
            {
                _logger.LogInformation("the service {Service} works again", ServiceChecks.Label(service));
            }
        }
    }

    // Writes the line of the services for the menu of the host when it changes.
    private void Write(ServicesReport report)
    {
        var line = ServiceChecks.Line(report.Services);
        if (string.Equals(line, _line, StringComparison.Ordinal))
        {
            return;
        }

        _line = line;
        try
        {
            File.WriteAllText(_file, line + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "the line of the services was not written to {Path}", _file);
        }
    }
}
