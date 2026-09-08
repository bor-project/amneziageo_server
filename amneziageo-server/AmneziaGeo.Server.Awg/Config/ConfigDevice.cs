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
        H1 = Span(settings.H1),
        H2 = Span(settings.H2),
        H3 = Span(settings.H3),
        H4 = Span(settings.H4),
        I1 = settings.I1,
        I2 = settings.I2,
        I3 = settings.I3,
        I4 = settings.I4,
        I5 = settings.I5,
        HeaderProtectionKey = settings.HeaderProtectionKey.Length > 0 ? settings.HeaderProtectionKey : null,
        ContentPaddingAddition = Span(settings.ContentPaddingAddition),
        RekeyAfterTime = Span(settings.RekeyAfterTime),
        RekeyTimeout = Span(settings.RekeyTimeout),
        RejectAfterTime = Span(settings.RejectAfterTime),
        KeepaliveTimeout = Span(settings.KeepaliveTimeout),
        MaxHandshakeAttempts = Span(settings.MaxHandshakeAttempts),
        RandomTrailers = settings.RandomTrailers,
        DisableCookies = settings.DisableCookies,
    };

    private static AwgRange Span(string value) => AwgRange.TryParse(value, out var range) ? range : default;
}
