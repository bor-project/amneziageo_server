using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Dal;
using AmneziaGeo.Server.Routing.Guard;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Whether the panel cuts off a second device that connects with the configuration of a first one.
/// </summary>
public sealed class GuardOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Guard";

    /// <summary>
    /// Whether the guard watches the interfaces.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Watches the peers of the endpoints and cuts off a second device that shares the configuration of a first one.
/// </summary>
public sealed class ClientGuard : BackgroundService
{
    private const uint Kick = 25;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Reread = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan KickFor = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopes;

    private readonly IAwgDevices _devices;

    private readonly IHostNetwork _network;

    private readonly GuardOptions _options;

    private readonly TimeProvider _time;

    private readonly ILogger<ClientGuard> _logger;

    private readonly PeerGuard _guard = new();

    private readonly Lock _sync = new();

    private readonly List<Settle> _settles = [];

    private IReadOnlyList<ServerConfig> _configs = [];

    private DateTimeOffset _read = DateTimeOffset.MinValue;

    private DateTimeOffset _laid = DateTimeOffset.MinValue;

    private string _ruleset = string.Empty;

    private bool _blind;

    private bool _refused;

    /// <summary>
    /// ctor
    /// </summary>
    public ClientGuard(
        IServiceScopeFactory scopes,
        IAwgDevices devices,
        IHostNetwork network,
        GuardOptions options,
        TimeProvider time,
        ILogger<ClientGuard> logger)
    {
        _scopes = scopes;
        _devices = devices;
        _network = network;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Returns the addresses a peer of an interface is kept cut off at.
    /// </summary>
    public IReadOnlyList<string> Cut(string name, string publicKey)
    {
        lock (_sync)
        {
            return _guard.Cut(name, publicKey);
        }
    }

    /// <summary>
    /// Tells whether a peer of an endpoint sent anything within the time the endpoint counts a client online, null
    /// when the guard has not heard it.
    /// </summary>
    public bool? Online(ServerConfig config, string publicKey)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            return _guard.Heard(config.Name, publicKey) is { } heard
                ? _time.GetUtcNow() - heard <= TimeSpan.FromSeconds(config.OfflineAfter)
                : null;
        }
    }

    /// <summary>
    /// Reads the interfaces once, cuts off the second devices it catches and keeps the firewall level with them.
    /// </summary>
    public async Task LookAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (now - _read > Reread)
        {
            using var scope = _scopes.CreateScope();
            _configs = await scope.ServiceProvider.GetRequiredService<ConfigStore>().ListAsync(ct).ConfigureAwait(false);
            _read = now;
        }

        var caught = new List<GuardCut>();
        foreach (var config in _configs.Where(one => one.IsEnabled))
        {
            if (Read(config.Name) is not { } device)
            {
                continue;
            }

            lock (_sync)
            {
                caught.AddRange(_guard.Observe(device, TimeSpan.FromSeconds(config.OfflineAfter), now));
            }
        }

        await LayAsync(now, ct).ConfigureAwait(false);
        foreach (var cut in caught)
        {
            Relay(cut, now);
        }

        SettleDue(now);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        try
        {
            using var timer = new PeriodicTimer(Tick, _time);
            do
            {
                try
                {
                    await LookAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    _logger.LogWarning(ex, "the guard did not read the interfaces");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private AwgDevice? Read(string name)
    {
        try
        {
            return _devices.Find(name);
        }
        catch (NetlinkException ex)
        {
            if (!_blind)
            {
                _logger.LogWarning(ex, "the guard does not read the interface {Interface}", name);
                _blind = true;
            }

            return null;
        }
    }

    private async Task LayAsync(DateTimeOffset now, CancellationToken ct)
    {
        var holds = Holds();
        var ruleset = GuardRuleset.Text(holds);
        var same = string.Equals(ruleset, _ruleset, StringComparison.Ordinal);
        if ((same && (holds.Count == 0 || now - _laid < Refresh)) || (_refused && now - _laid < Refresh))
        {
            return;
        }

        _laid = now;
        try
        {
            await _network.FirewallAsync(ruleset, ct).ConfigureAwait(false);
            _ruleset = ruleset;
            _refused = false;
        }
        catch (HostNetworkException ex)
        {
            if (!_refused)
            {
                _logger.LogWarning(ex, "the firewall did not take the guard");
            }

            _refused = true;
        }
    }

    private IReadOnlyList<GuardHold> Holds()
    {
        lock (_sync)
        {
            return _guard.Holds();
        }
    }

    private void Relay(GuardCut cut, DateTimeOffset now)
    {
        _logger.LogWarning(
            "the configuration of {Peer} on {Interface} came from a second device: {Cut} is cut off, {First} is kept",
            cut.Peer.PublicKey,
            cut.Interface,
            string.Join(", ", cut.Cut),
            cut.First);

        try
        {
            _devices.Apply(new AwgUpdate
            {
                Name = cut.Interface,
                Peers = [new AwgPeerUpdate { PublicKey = cut.Peer.PublicKey, Remove = true }],
            });
            _devices.Apply(new AwgUpdate
            {
                Name = cut.Interface,
                Peers =
                [
                    new AwgPeerUpdate
                    {
                        PublicKey = cut.Peer.PublicKey,
                        PresharedKey = cut.Peer.PresharedKey,
                        Endpoint = cut.First,
                        PersistentKeepalive = new AwgRange(Kick),
                        ReplaceAllowedIps = true,
                        AllowedIps = cut.Peer.AllowedIps,
                    },
                ],
            });
            _settles.Add(new Settle(cut.Interface, cut.Peer.PublicKey, cut.Peer.PersistentKeepalive, now + KickFor));
        }
        catch (NetlinkException ex)
        {
            _logger.LogWarning(ex, "the guard did not lay {Peer} on {Interface} again", cut.Peer.PublicKey, cut.Interface);
        }
    }

    private void SettleDue(DateTimeOffset now)
    {
        foreach (var settle in _settles.Where(one => one.At <= now).ToArray())
        {
            _settles.Remove(settle);
            try
            {
                _devices.Apply(new AwgUpdate
                {
                    Name = settle.Interface,
                    Peers =
                    [
                        new AwgPeerUpdate
                        {
                            PublicKey = settle.PublicKey,
                            UpdateOnly = true,
                            PersistentKeepalive = settle.Keepalive,
                        },
                    ],
                });
            }
            catch (NetlinkException ex)
            {
                _logger.LogWarning(ex, "the guard did not return the keepalive of {Peer}", settle.PublicKey);
            }
        }
    }

    private sealed record Settle(string Interface, string PublicKey, AwgRange Keepalive, DateTimeOffset At);
}
