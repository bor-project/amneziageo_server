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
}
