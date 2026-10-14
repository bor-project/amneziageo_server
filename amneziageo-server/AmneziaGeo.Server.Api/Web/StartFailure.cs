using Microsoft.AspNetCore.Connections;

namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Says why the panel did not start when another program holds an address it listens on.
/// </summary>
public static class StartFailure
{
    /// <summary>
    /// The code the panel leaves with when its address is taken.
    /// </summary>
    public const int PortTaken = 1;

    /// <summary>
    /// Tells whether the start failed because another program holds an address the panel listens on.
    /// </summary>
    public static bool IsPortTaken(Exception error) => error is IOException { InnerException: AddressInUseException };

    /// <summary>
    /// Names the taken address and the command that moves the panel off it.
    /// </summary>
    public static string Line(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return $"the panel did not start: {error.Message.TrimEnd('.')}; "
            + "free the port or move the panel with 'amneziageo-server panel set --port <port>'";
    }

    /// <summary>
    /// Stops what the failed start has set going, says why the panel did not start and lets the host go.
    /// </summary>
    public static async Task<int> LeaveAsync(IHost host, ILogger logger, Exception error)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        logger.LogDebug(error, "the panel did not bind its address");
        await host.StopAsync().ConfigureAwait(false);
        logger.LogCritical("{Reason}", Line(error));
        if (host is IAsyncDisposable gone)
        {
            await gone.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            host.Dispose();
        }

        return PortTaken;
    }
}
