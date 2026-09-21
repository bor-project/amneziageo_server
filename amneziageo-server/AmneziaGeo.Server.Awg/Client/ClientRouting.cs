namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Whether the application of a client routes on its own.
/// </summary>
public enum ClientRouting
{
    /// <summary>
    /// The client takes what its template says.
    /// </summary>
    Template = 0,

    /// <summary>
    /// The application of the client routes on its own.
    /// </summary>
    On = 1,

    /// <summary>
    /// The application of the client keeps the ranges of its file.
    /// </summary>
    Off = 2,
}

/// <summary>
/// Writes and reads whether the application of a client routes on its own.
/// </summary>
public static class RoutingName
{
    /// <summary>
    /// The name of taking what the template says.
    /// </summary>
    public const string Template = "template";

    /// <summary>
    /// The name of routing on the client.
    /// </summary>
    public const string On = "on";

    /// <summary>
    /// The name of keeping the ranges of the file.
    /// </summary>
    public const string Off = "off";

    /// <summary>
    /// Returns the name of what a client says about routing.
    /// </summary>
    public static string Of(ClientRouting routing) => routing switch
    {
        ClientRouting.On => On,
        ClientRouting.Off => Off,
        _ => Template,
    };

    /// <summary>
    /// Returns whether the application of a client routes on its own, reading the template where the client leaves
    /// the choice to it.
    /// </summary>
    public static bool Taken(ClientRouting client, ClientTemplate? template) => client switch
    {
        ClientRouting.On => true,
        ClientRouting.Off => false,
        _ => template?.Routing ?? TemplateDefaults.Routing,
    };

    /// <summary>
    /// Returns what a name stands for, the given choice where the name is empty and -1 where it is unknown.
    /// </summary>
    public static ClientRouting Read(string? text, ClientRouting otherwise)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return otherwise;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            Template => ClientRouting.Template,
            On => ClientRouting.On,
            Off => ClientRouting.Off,
            _ => (ClientRouting)(-1),
        };
    }
}
