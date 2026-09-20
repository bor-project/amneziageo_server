namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// The endpoint template a fresh database starts with.
/// </summary>
public static class InterfaceTemplateDefaults
{
    /// <summary>
    /// The name the built in template takes.
    /// </summary>
    public const string Name = "amnezia-3.1";

    /// <summary>
    /// Returns the built in template, with obfuscation no other install repeats.
    /// </summary>
    public static InterfaceTemplate Fresh() => new()
    {
        Name = Name,
        ListenPort = ConfigDefaults.ListenPort,
        Subnet = ConfigDefaults.Subnet,
        Dns = [.. ConfigDefaults.Dns],
        AllowedIps = [.. ConfigDefaults.AllowedIps],
        Mtu = ConfigDefaults.Mtu,
        Keepalive = ConfigDefaults.Keepalive,
        OfflineAfter = ConfigDefaults.OfflineAfter,
        Blocked = [.. ConfigDefaults.Blocked],
        Obfuscation = ConfigDefaults.Obfuscation(),
    };
}
