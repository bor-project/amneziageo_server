using AmneziaGeo.Server.Awg.Netlink;
using AmneziaGeo.Server.Routing.Host;

namespace AmneziaGeo.Server.Api.Outbounds;

/// <summary>
/// Hears the kernel tell of changes of the interfaces and of the routing rules of the host and passes them on to the
/// round that keeps the outbounds and the endpoints.
/// </summary>
public sealed class HostNews : IHostedService, IDisposable
{
    private static readonly TimeSpan JoinFor = TimeSpan.FromSeconds(2);

    private readonly HostChanges _changes;

    private readonly ILogger<HostNews> _logger;

    private RouteEvents? _socket;

    private Thread? _reader;

    private volatile bool _stopping;

    /// <summary>
    /// ctor
    /// </summary>
    public HostNews(HostChanges changes, ILogger<HostNews> logger)
    {
        _changes = changes;
        _logger = logger;
    }

    /// <summary>
    /// Opens the socket and starts reading it.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _socket = new RouteEvents();
        }
        catch (Exception ex) when (ex is NetlinkException or DllNotFoundException)
        {
            _logger.LogWarning(ex, "the panel does not hear the kernel about the interfaces and the rules and looks at them every round");

            return Task.CompletedTask;
        }

        _changes.Listening();
        _reader = new Thread(Read) { IsBackground = true, Name = "routing news" };
        _reader.Start();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ends the reading.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _socket?.Wake();
        _reader?.Join(JoinFor);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes the socket.
    /// </summary>
    public void Dispose() => _socket?.Dispose();

    private void Read()
    {
        var buffer = new byte[32768];
        while (!_stopping)
        {
            try
            {
                if (_socket!.Wait(buffer))
                {
                    _changes.Heard();
                }
            }
            catch (Exception ex) when (ex is NetlinkException or ObjectDisposedException)
            {
                if (!_stopping)
                {
                    _logger.LogWarning(ex, "the panel stopped hearing the kernel about the interfaces and the rules");
                    _changes.Deaf();
                }

                return;
            }
        }
    }
}
