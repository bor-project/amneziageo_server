using System.Net;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Routing.Outbound;

/// <summary>
/// The outbound a client configuration was read into, and why it was refused.
/// </summary>
/// <param name="Outbound">The outbound the text carried.</param>
/// <param name="Fault">The reason the text was refused.</param>
public sealed record OutboundImportResult(OutboundConfig? Outbound, OutboundFault? Fault);

/// <summary>
/// Reads the client configuration of an AmneziaWG server into an outbound.
/// </summary>
public static class OutboundImport
{
    private static readonly char[] Breaks = [',', ' ', '\t'];

    /// <summary>
    /// Reads a client configuration under the name the outbound takes.
    /// </summary>
    public static OutboundImportResult Read(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Refuse("bad-import", "the configuration is empty");
        }

        var settings = Sections(text);
        var self = settings.Interface;
        var peer = settings.Peer;

        if (!Curve25519.IsKey(Value(self, "PrivateKey")))
        {
            return Refuse("bad-import", "the configuration carries no private key");
        }

        if (!Curve25519.IsKey(Value(peer, "PublicKey")))
        {
            return Refuse("bad-import", "the configuration carries no public key of the server");
        }

        if (!Endpoint(Value(peer, "Endpoint"), out var host, out var port))
        {
            return Refuse("bad-import", "the configuration carries no address of the server");
        }

        var key = Value(self, "PrivateKey");

        return new OutboundImportResult(
            new OutboundConfig
            {
                Name = name,
                Kind = OutboundKind.Wg,
                Host = host,
                Port = port,
                PrivateKey = key,
                PublicKey = Curve25519.PublicOf(key),
                PeerKey = Value(peer, "PublicKey"),
                PresharedKey = Value(peer, "PresharedKey"),
                Address = Parts(Value(self, "Address")),
                Dns = [.. Parts(Value(self, "DNS")).Where(one => IPAddress.TryParse(one, out _))],
                Mtu = Number(self, "MTU", OutboundDefaults.Mtu),
                Keepalive = Number(peer, "PersistentKeepalive", OutboundDefaults.Keepalive),
                Obfuscation = Obfuscation(self),
            },
            null);
    }

    private static ObfuscationSettings Obfuscation(Dictionary<string, string> self) => new()
    {
        Jc = Number(self, "Jc", 0),
        Jmin = Number(self, "Jmin", 0),
        Jmax = Number(self, "Jmax", 0),
        S1 = Number(self, "S1", 0),
        S2 = Number(self, "S2", 0),
        S3 = Number(self, "S3", 0),
        S4 = Number(self, "S4", 0),
        H1 = Value(self, "H1"),
        H2 = Value(self, "H2"),
        H3 = Value(self, "H3"),
        H4 = Value(self, "H4"),
        I1 = Special(self, "I1"),
        I2 = Special(self, "I2"),
        I3 = Special(self, "I3"),
        I4 = Special(self, "I4"),
        I5 = Special(self, "I5"),
        HeaderProtectionKey = Value(self, "HeaderProtectionKey"),
        ContentPaddingAddition = Value(self, "ContentPaddingAddition"),
        RekeyAfterTime = Value(self, "RekeyAfterTime"),
        RekeyTimeout = Value(self, "RekeyTimeout"),
        RejectAfterTime = Value(self, "RejectAfterTime"),
        KeepaliveTimeout = Value(self, "KeepaliveTimeout"),
        MaxHandshakeAttempts = Value(self, "MaxHandshakeAttempts"),
        RandomTrailers = Flag(self, "RandomTrailers"),
        DisableCookies = Flag(self, "DisableCookies"),
    };

    private static (Dictionary<string, string> Interface, Dictionary<string, string> Peer) Sections(string text)
    {
        var self = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var peer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var current = self;

        foreach (var raw in text.Split('\n'))
        {
            var line = Trim(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                current = line.StartsWith("[Peer", StringComparison.OrdinalIgnoreCase) ? peer : self;

                continue;
            }

            var mark = line.IndexOf('=', StringComparison.Ordinal);
            if (mark > 0)
            {
                current[line[..mark].Trim()] = line[(mark + 1)..].Trim();
            }
        }

        return (self, peer);
    }

    private static bool Endpoint(string text, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (text.Length == 0)
        {
            return false;
        }

        var mark = text.LastIndexOf(':');
        if (mark <= 0 || !int.TryParse(text[(mark + 1)..], out port) || port is <= 0 or > 65535)
        {
            return false;
        }

        host = text[..mark].Trim('[', ']');

        return host.Length > 0;
    }

    private static string Trim(string line)
    {
        var body = line.Trim();
        var mark = body.IndexOf('#', StringComparison.Ordinal);

        return mark < 0 ? body : body[..mark].Trim();
    }

    private static string Value(Dictionary<string, string> section, string key) =>
        section.TryGetValue(key, out var found) ? found : string.Empty;

    private static string? Special(Dictionary<string, string> section, string key) =>
        section.TryGetValue(key, out var found) && found.Length > 0 ? found : null;

    private static int Number(Dictionary<string, string> section, string key, int fallback) =>
        section.TryGetValue(key, out var found) && int.TryParse(found, out var value) ? value : fallback;

    private static bool Flag(Dictionary<string, string> section, string key) =>
        section.TryGetValue(key, out var found)
        && (found.Equals("on", StringComparison.OrdinalIgnoreCase)
            || found.Equals("true", StringComparison.OrdinalIgnoreCase)
            || found.Equals("1", StringComparison.Ordinal));

    private static IReadOnlyList<string> Parts(string text) =>
        [.. text.Split(Breaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static OutboundImportResult Refuse(string code, string message) =>
        new(null, new OutboundFault(code, message));
}
