using AmneziaGeo.Server.Awg.Device;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Turns the settings of an endpoint into a change for the kernel.
/// </summary>
public static class ConfigDevice
{
    /// <summary>
    /// Returns the change that puts the settings of an endpoint on its interface.
    /// </summary>
    public static AwgUpdate Update(ServerConfig config) => new()
    {
        Name = config.Name,
        PrivateKey = config.PrivateKey,
        ListenPort = (ushort)config.ListenPort,
        Obfuscation = Obfuscation(config.Obfuscation),
    };

    /// <summary>
    /// Returns the obfuscation of an endpoint as the kernel takes it.
    /// </summary>
    public static AwgObfuscation Obfuscation(ObfuscationSettings settings) => new()
    {
        Jc = (ushort)settings.Jc,
        Jmin = (ushort)settings.Jmin,
        Jmax = (ushort)settings.Jmax,
        S1 = (ushort)settings.S1,
        S2 = (ushort)settings.S2,
        S3 = (ushort)settings.S3,
        S4 = (ushort)settings.S4,
        H1 = new AwgRange((uint)settings.H1),
        H2 = new AwgRange((uint)settings.H2),
        H3 = new AwgRange((uint)settings.H3),
        H4 = new AwgRange((uint)settings.H4),
        I1 = settings.I1,
        I2 = settings.I2,
        I3 = settings.I3,
        I4 = settings.I4,
        I5 = settings.I5,
        RandomTrailers = settings.RandomTrailers,
        DisableCookies = settings.DisableCookies,
    };
}
