using AmneziaGeo.Server.Api.Auth;
using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Api.Updates;

/// <summary>
/// Looks for releases of the panel and hands one over to the tool that puts it on.
/// </summary>
public sealed class UpdateCenter
{
    /// <summary>
    /// The name of the HTTP client releases are read with.
    /// </summary>
    public const string Client = "updates";

    /// <summary>
    /// Nothing runs.
    /// </summary>
    public const string Idle = "idle";

    /// <summary>
    /// The releases are looked over.
    /// </summary>
    public const string Checking = "checking";

    /// <summary>
    /// The release is downloaded.
    /// </summary>
    public const string Downloading = "downloading";

    /// <summary>
    /// The release is handed over to the tool that puts it on.
    /// </summary>
    public const string Starting = "starting";

    private readonly UpdateOptions _options;

    private readonly IHttpClientFactory _clients;

    private readonly TimeProvider _time;

    private readonly ILogger<UpdateCenter> _logger;

    private readonly IServiceScopeFactory _scopes;

    private readonly UpdateJournal _journal;

    private readonly SemaphoreSlim _checking = new(1, 1);

    private readonly Lock _gate = new();

    private readonly string _mode;

    private readonly string _data;

    private UpdateOffer? _offer;

    private DateTimeOffset? _checked;

    private Failure? _fault;

    private string _stage = Idle;

    private string _blocker = string.Empty;

    private bool _tests;

    private DockerPlace? _place;

    /// <summary>
    /// ctor
    /// </summary>
    public UpdateCenter(
        UpdateOptions options,
        IHttpClientFactory clients,
        TimeProvider time,
        ILogger<UpdateCenter> logger,
        IServiceScopeFactory scopes)
    {
        _options = options;
        _clients = clients;
        _time = time;
        _logger = logger;
        _scopes = scopes;
        _tests = options.TakesTests;
        _data = Path.GetDirectoryName(ServerDatabase.DefaultPath()) is { Length: > 0 } data ? data : "/var/lib/amneziageo-server";
        _journal = new UpdateJournal(options.Directory.Length > 0 ? options.Directory : Path.Combine(_data, "update"));
        _mode = UpdateModes.Detect(
            AppContext.BaseDirectory,
            File.Exists,
            Environment.GetEnvironmentVariable(UpdateModes.ContainerVariable));
    }

    /// <summary>
    /// The version of the panel that runs.
    /// </summary>
    public static Version Current { get; } = typeof(UpdateCenter).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Returns what the panel knows of its releases and of the update started last.
    /// </summary>
    public UpdateResponse Status()
    {
        var run = _journal.Read(Current, _time.GetUtcNow());
        lock (_gate)
        {
            return new UpdateResponse(
                Current.ToString(),
                _tests ? UpdateOptions.Test : UpdateOptions.Stable,
                _mode,
                _blocker,
                _checked,
                _offer is null
                    ? null
                    : new UpdateLatestResponse(_offer.Manifest.Version.ToString(), _offer.Manifest.Published, _offer.Notes),
                _stage,
                _fault,
                run is null
                    ? null
                    : new UpdateRunResponse(run.Record.From, run.Record.To, run.State, run.Record.Started, run.Log));
        }
    }

    /// <summary>
    /// Looks the releases over and tells whether the panel can put one on by itself.
    /// </summary>
    public async Task<UpdateResponse> CheckAsync(CancellationToken ct)
    {
        await _checking.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Move(Checking, Idle);
            Tidy();
            var tests = await TestsAsync(ct).ConfigureAwait(false);
            var blocker = await BlockerAsync(ct).ConfigureAwait(false);
            var (offer, fault) = await LookAsync(tests, ct).ConfigureAwait(false);
            lock (_gate)
            {
                _blocker = blocker;
                _checked = _time.GetUtcNow();
                _fault = fault;
                if (fault is null || tests != _tests)
                {
                    _offer = offer is not null && offer.Manifest.Version > Current ? offer : null;
                }

                _tests = tests;
            }
        }
        finally
        {
            Move(Idle, Checking);
            _checking.Release();
        }

        return Status();
    }

    /// <summary>
    /// Looks the releases over again in the background.
    /// </summary>
    public void Recheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await CheckAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "the releases of the panel were not looked over again");
            }
        });
    }

    /// <summary>
    /// Starts the move to a release found by the last look, or tells why it does not start.
    /// </summary>
    public Failure? Apply(string version)
    {
        var run = _journal.Read(Current, _time.GetUtcNow());
        lock (_gate)
        {
            if (_stage != Idle || run?.State == UpdateJournal.Running)
            {
                return new Failure("update-busy", "the panel is busy with an update");
            }

            if (_offer is null || !string.Equals(_offer.Manifest.Version.ToString(), version, StringComparison.Ordinal))
            {
                return new Failure("update-unknown", $"there is no release {version} to move to");
            }

            if (_blocker.Length > 0)
            {
                return new Failure("update-blocked", $"the panel cannot put a release on by itself: {_blocker}");
            }

            _stage = Downloading;
            var offer = _offer;
            var place = _place;
            _ = Task.Run(() => RunAsync(offer, place));
        }

        return null;
    }

    private async Task RunAsync(UpdateOffer offer, DockerPlace? place)
    {
        var from = Current.ToString();
        var to = offer.Manifest.Version.ToString();
        try
        {
            _journal.Begin(new UpdateRecord(from, to, _mode, _time.GetUtcNow()));
            if (_mode == UpdateModes.Docker && place is not null)
            {
                await DockerAsync(offer, place, from, to).ConfigureAwait(false);
            }
            else
            {
                await PackageAsync(offer, to).ConfigureAwait(false);
            }

            _logger.LogInformation("the update from {From} to {To} is handed over", from, to);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "the update from {From} to {To} did not start", from, to);
            Quietly(() => _journal.Refuse(to, Stamp(ex.Message)));
        }
        finally
        {
            Move(Idle, null);
        }
    }

    private async Task PackageAsync(UpdateOffer offer, string to)
    {
        var arch = UpdateModes.Arch();
        _journal.Note(to, Stamp($"downloading the package of {to} for {arch}"));
        var installer = await new PackageUpdater(_clients.CreateClient(Client))
            .StageAsync(offer, arch, _journal.Folder, CancellationToken.None)
            .ConfigureAwait(false);

        Move(Starting, null);
        _journal.Note(to, Stamp($"starting {installer}"));
        await PackageUpdater
            .LaunchAsync(installer, _journal.LogOf(to), _journal.ExitOf(to), "amneziageo-server-update-" + to, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task DockerAsync(UpdateOffer offer, DockerPlace place, string from, string to)
    {
        _journal.Note(to, Stamp($"pulling {offer.Manifest.Image}"));
        var image = await DockerUpdater.PullAsync(_options.Docker, place, offer.Manifest, CancellationToken.None)
            .ConfigureAwait(false);

        Move(Starting, null);
        _journal.Note(to, Stamp($"handing {place.Service} of {place.Project} over to {image}"));
        await DockerUpdater
            .StartAsync(_options.Docker, place, new DockerHandover(image, from, to, _data, _journal.Folder), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<string> BlockerAsync(CancellationToken ct)
    {
        if (_mode == UpdateModes.Package)
        {
            return PackageUpdater.Blocker();
        }

        if (_mode != UpdateModes.Docker)
        {
            return UpdateModes.Manual;
        }

        var (place, blocker) = await DockerUpdater.FindAsync(_options.Docker, _data, ct).ConfigureAwait(false);
        lock (_gate)
        {
            _place = place;
        }

        if (place is not null)
        {
            await SweepAsync(ct).ConfigureAwait(false);
        }

        return blocker;
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await DockerUpdater.SweepAsync(_options.Docker, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "the containers of earlier updates were not removed");
        }
    }

    private async Task<bool> TestsAsync(CancellationToken ct)
    {
        if (_options.TakesTests)
        {
            return true;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<PanelStore>().ReadAsync(ct).ConfigureAwait(false);

            return settings.Prereleases;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "the settings of the panel were not read");
            lock (_gate)
            {
                return _tests;
            }
        }
    }

    private async Task<(UpdateOffer? Offer, Failure? Fault)> LookAsync(bool tests, CancellationToken ct)
    {
        try
        {
            var key = UpdateSignature.Key(_options);
            if (key.Length == 0)
            {
                return (null, new Failure("update-no-key", "the panel carries no key to check releases against"));
            }

            var feed = new UpdateFeed(_clients.CreateClient(Client), _options, tests);

            return (await feed.NewestAsync(key, ct).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "the releases of the panel were not read");

            return (null, new Failure("update-check-failed", ex.Message));
        }
    }

    private void Tidy()
    {
        if (_mode != UpdateModes.Package || _journal.Read(Current, _time.GetUtcNow())?.State == UpdateJournal.Running)
        {
            return;
        }

        Quietly(() => PackageUpdater.Sweep(_journal.Folder));
    }

    private void Move(string stage, string? from)
    {
        lock (_gate)
        {
            if (from is null || _stage == from)
            {
                _stage = stage;
            }
        }
    }

    private string Stamp(string line) => $"{_time.GetUtcNow():HH:mm:ss} {line}";

    private void Quietly(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "the files of the update were not written");
        }
    }
}
