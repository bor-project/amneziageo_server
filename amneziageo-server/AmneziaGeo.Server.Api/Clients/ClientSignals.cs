using System.Collections.Concurrent;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Api.Clients;

/// <summary>
/// Whom the panel sends the signal to disconnect.
/// </summary>
public sealed class SignalOptions
{
    /// <summary>
    /// The section the settings are read from.
    /// </summary>
    public const string Section = "Signal";

    /// <summary>
    /// Whether the signal goes to a client the panel does not see connected as well.
    /// </summary>
    public bool Always { get; set; }
}

/// <summary>
/// A signal to disconnect the application of a client did not take.
/// </summary>
/// <param name="At">When the signal was sent.</param>
/// <param name="Error">The code of the failure.</param>
/// <param name="Message">What went wrong, in words.</param>
public sealed record SignalMiss(DateTimeOffset At, string Error, string Message);

/// <summary>
/// Sends the signal to disconnect to the clients that are being turned off and keeps the ones it did not reach.
/// </summary>
public sealed class ClientSignals
{
    private readonly DisconnectSignal _signal;

    private readonly Func<ServerConfig, TunnelClient, bool> _connected;

    private readonly TimeProvider _time;

    private readonly ILogger<ClientSignals> _logger;

    private readonly ConcurrentDictionary<long, SignalMiss> _missed = new();

    /// <summary>
    /// ctor
    /// </summary>
    public ClientSignals(
        DisconnectSignal signal,
        Func<ServerConfig, TunnelClient, bool> connected,
        TimeProvider time,
        ILogger<ClientSignals> logger)
    {
        _signal = signal;
        _connected = connected;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Returns the signal a client did not take, null when it took the last one or was sent none.
    /// </summary>
    public SignalMiss? Missed(long clientId) => _missed.GetValueOrDefault(clientId);

    /// <summary>
    /// Forgets the signal a client did not take.
    /// </summary>
    public void Forget(long clientId) => _missed.TryRemove(clientId, out _);

    /// <summary>
    /// Tells the applications of the clients that are on and connected to take their tunnels down, while their
    /// endpoints still carry them.
    /// </summary>
    public async Task SendAsync(IReadOnlyList<TunnelClient> clients, IReadOnlyList<ServerConfig> endpoints, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(endpoints);

        var due = new List<Task>();
        foreach (var client in clients.Where(one => one.IsEnabled))
        {
            var endpoint = endpoints.FirstOrDefault(one => one.Id == client.ConfigId);
            if (endpoint is { IsEnabled: true } && _connected(endpoint, client))
            {
                due.Add(SendOneAsync(endpoint, client, ct));
            }
        }

        await Task.WhenAll(due).ConfigureAwait(false);
    }

    private async Task SendOneAsync(ServerConfig endpoint, TunnelClient client, CancellationToken ct)
    {
        var outcome = await _signal.SendAsync(endpoint, client, ct).ConfigureAwait(false);
        if (outcome.IsTaken)
        {
            _missed.TryRemove(client.Id, out _);
            _logger.LogInformation("the application of {Client} on {Endpoint} took the signal to disconnect", client.Name, endpoint.Name);

            return;
        }

        _missed[client.Id] = new SignalMiss(_time.GetUtcNow(), outcome.Error, outcome.Message);
        _logger.LogWarning(
            "the application of {Client} on {Endpoint} did not take the signal to disconnect: {Reason}",
            client.Name,
            endpoint.Name,
            outcome.Message);
    }
}
