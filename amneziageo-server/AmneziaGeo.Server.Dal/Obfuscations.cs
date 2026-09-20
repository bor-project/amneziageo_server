using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Dal;

/// <summary>
/// Carries obfuscation between a row and the settings the panel works with.
/// </summary>
internal static class Obfuscations
{
    /// <summary>
    /// Returns the obfuscation a row holds.
    /// </summary>
    internal static ObfuscationSettings Of(IObfuscated row) => new()
    {
        Jc = row.Jc,
        Jmin = row.Jmin,
        Jmax = row.Jmax,
        S1 = row.S1,
        S2 = row.S2,
        S3 = row.S3,
        S4 = row.S4,
        H1 = row.H1,
        H2 = row.H2,
        H3 = row.H3,
        H4 = row.H4,
        I1 = row.I1,
        I2 = row.I2,
        I3 = row.I3,
        I4 = row.I4,
        I5 = row.I5,
        HeaderProtectionKey = row.HeaderProtectionKey,
        ContentPaddingAddition = row.ContentPaddingAddition,
        RekeyAfterTime = row.RekeyAfterTime,
        RekeyTimeout = row.RekeyTimeout,
        RejectAfterTime = row.RejectAfterTime,
        KeepaliveTimeout = row.KeepaliveTimeout,
        MaxHandshakeAttempts = row.MaxHandshakeAttempts,
        RandomTrailers = row.RandomTrailers,
        DisableCookies = row.DisableCookies,
    };

    /// <summary>
    /// Puts the obfuscation into a row.
    /// </summary>
    internal static void Write(IObfuscated row, ObfuscationSettings obfuscation)
    {
        row.Jc = obfuscation.Jc;
        row.Jmin = obfuscation.Jmin;
        row.Jmax = obfuscation.Jmax;
        row.S1 = obfuscation.S1;
        row.S2 = obfuscation.S2;
        row.S3 = obfuscation.S3;
        row.S4 = obfuscation.S4;
        row.H1 = obfuscation.H1;
        row.H2 = obfuscation.H2;
        row.H3 = obfuscation.H3;
        row.H4 = obfuscation.H4;
        row.I1 = Text(obfuscation.I1);
        row.I2 = Text(obfuscation.I2);
        row.I3 = Text(obfuscation.I3);
        row.I4 = Text(obfuscation.I4);
        row.I5 = Text(obfuscation.I5);
        row.HeaderProtectionKey = obfuscation.HeaderProtectionKey.Trim();
        row.ContentPaddingAddition = obfuscation.ContentPaddingAddition.Trim();
        row.RekeyAfterTime = obfuscation.RekeyAfterTime.Trim();
        row.RekeyTimeout = obfuscation.RekeyTimeout.Trim();
        row.RejectAfterTime = obfuscation.RejectAfterTime.Trim();
        row.KeepaliveTimeout = obfuscation.KeepaliveTimeout.Trim();
        row.MaxHandshakeAttempts = obfuscation.MaxHandshakeAttempts.Trim();
        row.RandomTrailers = obfuscation.RandomTrailers;
        row.DisableCookies = obfuscation.DisableCookies;
    }

    /// <summary>
    /// Returns one line the values of an obfuscation amount to.
    /// </summary>
    internal static string Line(ObfuscationSettings obfuscation)
    {
        ArgumentNullException.ThrowIfNull(obfuscation);

        return string.Join(
            '|',
            obfuscation.Jc,
            obfuscation.Jmin,
            obfuscation.Jmax,
            obfuscation.S1,
            obfuscation.S2,
            obfuscation.S3,
            obfuscation.S4,
            obfuscation.H1,
            obfuscation.H2,
            obfuscation.H3,
            obfuscation.H4,
            obfuscation.I1,
            obfuscation.I2,
            obfuscation.I3,
            obfuscation.I4,
            obfuscation.I5,
            obfuscation.HeaderProtectionKey,
            obfuscation.ContentPaddingAddition,
            obfuscation.RekeyAfterTime,
            obfuscation.RekeyTimeout,
            obfuscation.RejectAfterTime,
            obfuscation.KeepaliveTimeout,
            obfuscation.MaxHandshakeAttempts,
            obfuscation.RandomTrailers,
            obfuscation.DisableCookies);
    }

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
