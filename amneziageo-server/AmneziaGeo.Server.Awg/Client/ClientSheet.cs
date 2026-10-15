namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// One configuration of a subscription as its page shows it.
/// </summary>
/// <param name="Name">The name the configuration goes by.</param>
/// <param name="Interface">The endpoint the configuration connects to.</param>
/// <param name="Endpoint">The address and port of the endpoint, empty while it names no host.</param>
/// <param name="FileName">The name the configuration is saved under.</param>
/// <param name="Text">The configuration as a file.</param>
/// <param name="Link">The configuration as a vpn:// link.</param>
public sealed record ClientSheet(string Name, string Interface, string Endpoint, string FileName, string Text, string Link);
