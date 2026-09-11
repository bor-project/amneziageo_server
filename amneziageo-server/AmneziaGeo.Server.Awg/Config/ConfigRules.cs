using System.Net;
using System.Text.RegularExpressions;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Config;

/// <summary>
/// Shapes the settings of an endpoint have to take.
/// </summary>
public static partial class ConfigRules
{
    /// <summary>
    /// The longest name an interface takes.
    /// </summary>
    public const int MaxNameLength = 15;

    /// <summary>
    /// The longest address clients reach the endpoint at.
    /// </summary>
    public const int MaxHostLength = 255;

    /// <summary>
    /// The smallest packet size an endpoint gives clients.
    /// </summary>
    public const int MinMtu = 576;

    /// <summary>
    /// The largest packet size an endpoint gives clients.
    /// </summary>
    public const int MaxMtu = 9000;

    /// <summary>
    /// The longest a client waits between empty packets, in seconds.
    /// </summary>
    public const int MaxKeepalive = 65535;

    /// <summary>
    /// The largest junk packet, in bytes.
    /// </summary>
    public const int MaxJunk = 1280;

    /// <summary>
    /// The most junk packets that go before a handshake.
    /// </summary>
    public const int MaxJunkCount = 128;

    /// <summary>
    /// The difference in size that would make an initiation and a response alike.
    /// </summary>
    public const int HandshakeGap = 56;

    /// <summary>
    /// The lowest packet type the obfuscation takes, above the four WireGuard holds.
    /// </summary>
    public const int LowestType = 5;

    /// <summary>
    /// The highest packet type the obfuscation takes.
    /// </summary>
    public const int HighestType = int.MaxValue;

    /// <summary>
    /// The longest special junk packet.
    /// </summary>
    public const int MaxSpecialLength = 1024;

    /// <summary>
    /// Returns why the settings of an endpoint are unusable, or null when they hold.
    /// </summary>
    public static ConfigFault? Check(ServerConfig config) =>
        CheckName(config.Name)
        ?? CheckHost(config.Host)
        ?? CheckPort(config.ListenPort)
        ?? CheckRanges(config.Address, "bad-address", "the interface")
        ?? CheckRanges(config.AllowedIps, "bad-allowed", "the client")
        ?? CheckBlocked(config.Blocked)
        ?? CheckServers(config.Dns)
        ?? CheckMtu(config.Mtu)
        ?? CheckKeepalive(config.Keepalive)
        ?? CheckOfflineAfter(config.OfflineAfter, config.Keepalive)
        ?? CheckKey(config.PrivateKey)
        ?? CheckPreshared(config.PresharedKey)
        ?? CheckObfuscation(config.Obfuscation);

    /// <summary>
    /// The shortest silence after which a device counts as gone, in seconds.
    /// </summary>
    public const int MinOfflineAfter = 10;

    /// <summary>
    /// The longest silence after which a device counts as gone, in seconds.
    /// </summary>
    public const int MaxOfflineAfter = 3600;

    /// <summary>
    /// Returns why the silence after which a device counts as gone is unusable, or null when it holds.
    /// </summary>
    public static ConfigFault? CheckOfflineAfter(int seconds, int keepalive)
    {
        if (seconds is < MinOfflineAfter or > MaxOfflineAfter)
        {
            return Fault("bad-offline-after", $"the silence is outside {MinOfflineAfter} to {MaxOfflineAfter} seconds");
        }

        return keepalive > 0 && seconds < keepalive * 2
            ? Fault("bad-offline-after", "the silence is shorter than two keepalive intervals")
            : null;
    }

    /// <summary>
    /// Returns why the name of an interface is unusable, or null when it holds.
    /// </summary>
    public static ConfigFault? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fault("bad-interface-name", "the name is empty");
        }

        if (name.Length > MaxNameLength)
        {
            return Fault("bad-interface-name", $"the name is longer than {MaxNameLength} characters");
        }

        return NameShape().IsMatch(name)
            ? null
            : Fault(
                "bad-interface-name",
                "the name takes lower case letters, digits, dash and underscore, and starts with a letter");
    }

    /// <summary>
    /// Returns why the ranges kept away from clients are unusable, or null when they hold.
    /// </summary>
    public static ConfigFault? CheckBlocked(IReadOnlyList<string> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        foreach (var range in ranges)
        {
            if (!AwgAllowedIp.TryParse(range, out _))
            {
                return Fault("bad-blocked", $"'{range}' is not an address range");
            }
        }

        return null;
    }

    /// <summary>
    /// Returns why the obfuscation is unusable, or null when it holds.
    /// </summary>
    public static ConfigFault? CheckObfuscation(ObfuscationSettings obfuscation)
    {
        if (obfuscation.Jc is < 0 or > MaxJunkCount)
        {
            return Fault("bad-junk", $"the junk packet count is outside 0 to {MaxJunkCount}");
        }

        if (Outside(obfuscation.Jmin) || Outside(obfuscation.Jmax) || obfuscation.Jmin > obfuscation.Jmax)
        {
            return Fault(
                "bad-junk",
                $"the junk packet size is outside 0 to {MaxJunk}, or the shortest one is above the longest");
        }

        foreach (var size in (int[])[obfuscation.S1, obfuscation.S2, obfuscation.S3, obfuscation.S4])
        {
            if (Outside(size))
            {
                return Fault("bad-junk", $"the junk prepended to a packet is outside 0 to {MaxJunk}");
            }
        }

        if (obfuscation.S1 + HandshakeGap == obfuscation.S2)
        {
            return Fault(
                "bad-junk",
                $"the junk of a response is {HandshakeGap} bytes above the junk of an initiation, which makes the two alike");
        }

        return CheckTypes(obfuscation) ?? CheckSpecials(obfuscation) ?? CheckSpans(obfuscation);
    }

    private static ConfigFault? CheckTypes(ObfuscationSettings obfuscation)
    {
        var types = new List<AwgRange>(4);
        foreach (var text in (string[])[obfuscation.H1, obfuscation.H2, obfuscation.H3, obfuscation.H4])
        {
            if (!AwgRange.TryParse(text, out var type))
            {
                return Fault("bad-type", "a packet type is neither a number nor a span of two");
            }

            if (type.Low < LowestType || type.High > HighestType)
            {
                return Fault("bad-type", $"a packet type is outside {LowestType} to {HighestType}");
            }

            types.Add(type);
        }

        return Apart(types) ? null : Fault("bad-type", "two packet types are the same");
    }

    private static ConfigFault? CheckSpans(ObfuscationSettings obfuscation)
    {
        var spans = (string[])
        [
            obfuscation.ContentPaddingAddition,
            obfuscation.RekeyAfterTime,
            obfuscation.RekeyTimeout,
            obfuscation.RejectAfterTime,
            obfuscation.KeepaliveTimeout,
            obfuscation.MaxHandshakeAttempts,
        ];

        foreach (var span in spans)
        {
            if (span.Length > 0 && !AwgRange.TryParse(span, out _))
            {
                return Fault("bad-span", "a timing is neither a number nor a span of two");
            }
        }

        return obfuscation.HeaderProtectionKey.Length == 0 || Curve25519.IsKey(obfuscation.HeaderProtectionKey)
            ? null
            : Fault("bad-header-key", "the header protection key is not 32 bytes in base64");
    }

    private static bool Apart(IReadOnlyList<AwgRange> types)
    {
        for (var one = 0; one < types.Count; one++)
        {
            for (var other = one + 1; other < types.Count; other++)
            {
                if (types[one].Low <= types[other].High && types[other].Low <= types[one].High)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ConfigFault? CheckSpecials(ObfuscationSettings obfuscation)
    {
        var specials = (string?[])[obfuscation.I1, obfuscation.I2, obfuscation.I3, obfuscation.I4, obfuscation.I5];

        return specials.Any(special => special is { Length: > MaxSpecialLength })
            ? Fault("bad-special", $"a special junk packet is longer than {MaxSpecialLength} characters")
            : null;
    }

    /// <summary>
    /// Returns why the address clients reach an endpoint at is unusable, or null when it holds.
    /// </summary>
    public static ConfigFault? CheckHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (host.Length > MaxHostLength)
        {
            return Fault("bad-host", $"the address is longer than {MaxHostLength} characters");
        }

        return IPAddress.TryParse(host, out _) || HostShape().IsMatch(host)
            ? null
            : Fault("bad-host", $"'{host}' is neither an address nor a host name");
    }

    private static ConfigFault? CheckPort(int port) =>
        port is > 0 and <= 65535 ? null : Fault("bad-port", "the port is outside 1 to 65535");

    private static ConfigFault? CheckRanges(IReadOnlyList<string> ranges, string code, string owner)
    {
        if (ranges.Count == 0)
        {
            return Fault(code, $"{owner} carries no address range");
        }

        foreach (var range in ranges)
        {
            if (!AwgAllowedIp.TryParse(range, out _))
            {
                return Fault(code, $"'{range}' is not an address range");
            }
        }

        return null;
    }

    private static ConfigFault? CheckServers(IReadOnlyList<string> servers)
    {
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out _))
            {
                return Fault("bad-dns", $"'{server}' is not a name server address");
            }
        }

        return null;
    }

    private static ConfigFault? CheckMtu(int mtu) =>
        mtu == 0 || mtu is >= MinMtu and <= MaxMtu
            ? null
            : Fault("bad-mtu", $"the packet size is outside {MinMtu} to {MaxMtu}");

    private static ConfigFault? CheckKeepalive(int keepalive) =>
        keepalive is >= 0 and <= MaxKeepalive
            ? null
            : Fault("bad-keepalive", $"the keepalive is outside 0 to {MaxKeepalive}");

    private static ConfigFault? CheckKey(string? key) =>
        Curve25519.IsKey(key) ? null : Fault("bad-key", "the private key is not 32 bytes in base64");

    private static ConfigFault? CheckPreshared(string? key) =>
        string.IsNullOrEmpty(key) || Curve25519.IsKey(key)
            ? null
            : Fault("bad-preshared", "the preshared key is not 32 bytes in base64");

    private static ConfigFault Fault(string code, string message) => new(code, message);

    private static bool Outside(int size) => size is < 0 or > MaxJunk;

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex NameShape();

    [GeneratedRegex("^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?([.][a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*$")]
    private static partial Regex HostShape();
}
