namespace AmneziaGeo.Server.Core.Panel;

/// <summary>
/// What the releases of the client are served with when nothing is set.
/// </summary>
public static class ClientUpdateDefaults
{
    /// <summary>
    /// The source that is a store of the panel.
    /// </summary>
    public const string Own = "own";

    /// <summary>
    /// The source that is a service standing apart.
    /// </summary>
    public const string Address = "address";

    /// <summary>
    /// The folder beside the database the store of the panel keeps the releases in.
    /// </summary>
    public const string Folder = "client-updates";

    /// <summary>
    /// The sources the releases can come from.
    /// </summary>
    public static readonly string[] Sources = [Own, Address];

    /// <summary>
    /// The settings the releases start with when the panel holds none.
    /// </summary>
    public static readonly ClientUpdateSettings Settings = new();
}
