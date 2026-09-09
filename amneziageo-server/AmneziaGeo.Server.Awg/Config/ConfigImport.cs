using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// The endpoint an interface file was read into, and why it was refused.
/// </summary>
/// <param name="Config">The endpoint the file carried.</param>
/// <param name="Fault">The reason the file was refused.</param>
public sealed record ConfigImportResult(ServerConfig? Config, ConfigFault? Fault);

/// <summary>
/// Reads the interface file of an AmneziaWG server into an endpoint.
/// </summary>
public static class ConfigImport
{
    /// <summary>
    /// Reads an interface file under the name the endpoint takes.
    /// </summary>
    public static ConfigImportResult Read(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Refuse("bad-import", "the interface file is empty");
        }

        var self = ConfigLines.Head(text);
        var key = ConfigLines.Value(self, "PrivateKey");
        if (!Curve25519.IsKey(key))
        {
            return Refuse("bad-import", "the interface file carries no private key");
        }

        return new ConfigImportResult(
            new ServerConfig
            {
                Name = name,
                ListenPort = ConfigLines.Number(self, "ListenPort", ConfigDefaults.ListenPort),
                Address = ConfigLines.Parts(ConfigLines.Value(self, "Address")),
                Dns = [.. ConfigDefaults.Dns],
                AllowedIps = [.. ConfigDefaults.AllowedIps],
                Mtu = ConfigLines.Number(self, "MTU", ConfigDefaults.Mtu),
                Keepalive = ConfigDefaults.Keepalive,
                IsEnabled = true,
                Nat = true,
                Blocked = [.. ConfigDefaults.Blocked],
                PrivateKey = key,
                PublicKey = Curve25519.PublicOf(key),
                Obfuscation = Obfuscation(self),
            },
            null);
    }

    private static ObfuscationSettings Obfuscation(IReadOnlyDictionary<string, string> self) => new()
    {
        Jc = ConfigLines.Number(self, "Jc", 0),
        Jmin = ConfigLines.Number(self, "Jmin", 0),
        Jmax = ConfigLines.Number(self, "Jmax", 0),
        S1 = ConfigLines.Number(self, "S1", 0),
        S2 = ConfigLines.Number(self, "S2", 0),
        S3 = ConfigLines.Number(self, "S3", 0),
        S4 = ConfigLines.Number(self, "S4", 0),
        H1 = ConfigLines.Value(self, "H1"),
        H2 = ConfigLines.Value(self, "H2"),
        H3 = ConfigLines.Value(self, "H3"),
        H4 = ConfigLines.Value(self, "H4"),
        I1 = Special(self, "I1"),
        I2 = Special(self, "I2"),
        I3 = Special(self, "I3"),
        I4 = Special(self, "I4"),
        I5 = Special(self, "I5"),
        HeaderProtectionKey = ConfigLines.Value(self, "HeaderProtectionKey"),
        ContentPaddingAddition = ConfigLines.Value(self, "ContentPaddingAddition"),
        RekeyAfterTime = ConfigLines.Value(self, "RekeyAfterTime"),
        RekeyTimeout = ConfigLines.Value(self, "RekeyTimeout"),
        RejectAfterTime = ConfigLines.Value(self, "RejectAfterTime"),
        KeepaliveTimeout = ConfigLines.Value(self, "KeepaliveTimeout"),
        MaxHandshakeAttempts = ConfigLines.Value(self, "MaxHandshakeAttempts"),
        RandomTrailers = ConfigLines.Flag(self, "RandomTrailers"),
        DisableCookies = ConfigLines.Flag(self, "DisableCookies"),
    };

    private static string? Special(IReadOnlyDictionary<string, string> self, string key) =>
        ConfigLines.Value(self, key) is { Length: > 0 } found ? found : null;

    private static ConfigImportResult Refuse(string code, string message) =>
        new(null, new ConfigFault(code, message));
}
