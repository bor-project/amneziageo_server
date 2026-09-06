using System.Net;
using System.Security.Cryptography;
using AmneziaGeo.Server.Awg.Device;
using AmneziaGeo.Server.Awg.Netlink;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The interface commands of the utility.
/// </summary>
public static class DeviceCommands
{
    /// <summary>
    /// Runs one interface command and returns the exit code.
    /// </summary>
    public static int Run(Arguments args)
    {
        try
        {
            return args.At(1) switch
            {
                "list" => List(),
                "show" => Show(args),
                "set" => Set(args),
                "peer" => Peer(args),
                _ => Usage(),
            };
        }
        catch (NetlinkException ex)
        {
            Terminal.Fail(ex.Message);

            return 1;
        }
    }

    private static int List()
    {
        using var devices = new AwgDevices();
        var found = devices.Names();
        if (found.Count == 0)
        {
            Terminal.Say("the host carries no amneziawg interface");

            return 0;
        }

        foreach (var name in found)
        {
            Terminal.Say(name);
        }

        return 0;
    }

    private static int Show(Arguments args)
    {
        using var devices = new AwgDevices();
        var names = Asked(devices, args.At(2));
        if (names.Count == 0)
        {
            Terminal.Say("the host carries no amneziawg interface");

            return 0;
        }

        var secrets = args.Has("secrets");
        var missing = 0;
        foreach (var name in names)
        {
            var device = devices.Find(name);
            if (device is null)
            {
                Terminal.Fail($"there is no amneziawg interface called '{name}'");
                missing++;

                continue;
            }

            Print(device, secrets);
        }

        return missing == 0 ? 0 : 1;
    }

    private static int Set(Arguments args)
    {
        var name = args.At(2);
        if (name is null)
        {
            return Usage();
        }

        var key = args.Has("generate") ? Fresh() : args.Value("key");
        var update = new AwgUpdate
        {
            Name = name,
            PrivateKey = key,
            ListenPort = Port(args.Value("port")),
            Fwmark = Count(args.Value("fwmark")),
            Obfuscation = Shape(args),
        };

        using var devices = new AwgDevices();
        devices.Apply(update);

        if (args.Has("generate"))
        {
            Terminal.Say($"private key: {key}");
        }

        Terminal.Say($"{name} is set");

        return 0;
    }

    private static int Peer(Arguments args)
    {
        var name = args.At(2);
        var key = args.At(3);
        if (name is null || key is null)
        {
            return Usage();
        }

        var update = new AwgUpdate
        {
            Name = name,
            Peers =
            [
                new AwgPeerUpdate
                {
                    PublicKey = key,
                    PresharedKey = args.Value("preshared"),
                    Endpoint = Point(args.Value("endpoint")),
                    PersistentKeepalive = Count(args.Value("keepalive")) is { } seconds ? new AwgRange(seconds) : null,
                    Remove = args.Has("remove"),
                    UpdateOnly = args.Has("update-only"),
                    ReplaceAllowedIps = args.Has("replace-ips"),
                    AllowedIps = Ranges(args.Value("allowed")),
                },
            ],
        };

        using var devices = new AwgDevices();
        devices.Apply(update);

        Terminal.Say($"{(args.Has("remove") ? "removed" : "set")} the peer of {name}");

        return 0;
    }

    private static AwgObfuscation? Shape(Arguments args)
    {
        var text = args.Value("shape");
        if (text is null)
        {
            return null;
        }

        var written = Pairs(text);

        return new AwgObfuscation
        {
            Jc = Word(written, "jc"),
            Jmin = Word(written, "jmin"),
            Jmax = Word(written, "jmax"),
            S1 = Word(written, "s1"),
            S2 = Word(written, "s2"),
            S3 = Word(written, "s3"),
            S4 = Word(written, "s4"),
            H1 = Span(written, "h1"),
            H2 = Span(written, "h2"),
            H3 = Span(written, "h3"),
            H4 = Span(written, "h4"),
            I1 = args.Value("i1"),
            I2 = args.Value("i2"),
            I3 = args.Value("i3"),
            I4 = args.Value("i4"),
            I5 = args.Value("i5"),
            HeaderProtectionKey = args.Value("hpk"),
            ContentPaddingAddition = Span(written, "padding"),
            RekeyAfterTime = Span(written, "rekey-after"),
            RekeyTimeout = Span(written, "rekey-timeout"),
            RejectAfterTime = Span(written, "reject-after"),
            KeepaliveTimeout = Span(written, "keepalive"),
            MaxHandshakeAttempts = Span(written, "attempts"),
            RandomTrailers = string.Equals(Text(written, "trailers"), "on", StringComparison.Ordinal),
            DisableCookies = string.Equals(Text(written, "cookies"), "off", StringComparison.Ordinal),
        };
    }

    private static Dictionary<string, string> Pairs(string text)
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var mark = part.IndexOf('=', StringComparison.Ordinal);
            if (mark > 0)
            {
                written[part[..mark].Trim()] = part[(mark + 1)..].Trim();
            }
        }

        return written;
    }

    private static string? Text(Dictionary<string, string> written, string name) =>
        written.TryGetValue(name, out var found) ? found : null;

    private static ushort Word(Dictionary<string, string> written, string name) =>
        ushort.TryParse(Text(written, name), out var found) ? found : (ushort)0;

    private static AwgRange Span(Dictionary<string, string> written, string name)
    {
        var text = Text(written, name);
        if (text is null)
        {
            return default;
        }

        var mark = text.IndexOf('-', StringComparison.Ordinal);
        if (mark < 0)
        {
            return uint.TryParse(text, out var one) ? new AwgRange(one) : default;
        }

        return uint.TryParse(text[..mark], out var low) && uint.TryParse(text[(mark + 1)..], out var high)
            ? new AwgRange(low, high)
            : default;
    }

    private static string Fresh()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        key[0] &= 248;
        key[31] &= 127;
        key[31] |= 64;

        return Convert.ToBase64String(key);
    }

    private static IReadOnlyList<AwgAllowedIp> Ranges(string? text) => text is null
        ? []
        : [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(AwgAllowedIp.Parse)];

    private static IPEndPoint? Point(string? text) => text is null ? null : IPEndPoint.Parse(text);

    private static ushort? Port(string? text) => ushort.TryParse(text, out var found) ? found : null;

    private static uint? Count(string? text) => uint.TryParse(text, out var found) ? found : null;

    private static void Print(AwgDevice device, bool secrets)
    {
        var shape = device.Obfuscation;

        Terminal.Say($"interface: {device.Name}");
        Terminal.Say($"  index: {device.Index}");
        Terminal.Say($"  public key: {device.PublicKey ?? "(none)"}");
        Terminal.Say($"  private key: {Private(device, secrets)}");
        Terminal.Say($"  listening port: {device.ListenPort}");
        Terminal.Say($"  fwmark: {(device.Fwmark == 0 ? "off" : $"0x{device.Fwmark:x}")}");
        Terminal.Say($"  junk packets: {shape.Jc} of {shape.Jmin} to {shape.Jmax} bytes");
        Terminal.Say($"  junk headers: s1 {shape.S1}, s2 {shape.S2}, s3 {shape.S3}, s4 {shape.S4}");
        Terminal.Say($"  packet types: h1 {shape.H1}, h2 {shape.H2}, h3 {shape.H3}, h4 {shape.H4}");
        Terminal.Say($"  special junk: {Junk(shape)}");
        Terminal.Say($"  header protection: {(shape.HeaderProtectionKey is null ? "off" : secrets ? shape.HeaderProtectionKey : "on")}");
        Terminal.Say($"  content padding: {shape.ContentPaddingAddition}");
        Terminal.Say($"  timers: rekey after {shape.RekeyAfterTime}s, rekey timeout {shape.RekeyTimeout}s, reject after {shape.RejectAfterTime}s, keepalive {shape.KeepaliveTimeout}s");
        Terminal.Say($"  handshake attempts: {shape.MaxHandshakeAttempts}");
        Terminal.Say($"  random trailers: {(shape.RandomTrailers ? "on" : "off")}, cookies: {(shape.DisableCookies ? "off" : "on")}");

        foreach (var peer in device.Peers)
        {
            Terminal.Say(string.Empty);
            Terminal.Say($"peer: {peer.PublicKey}");
            Terminal.Say($"  preshared key: {(peer.PresharedKey is null ? "(none)" : secrets ? peer.PresharedKey : "(hidden)")}");
            Terminal.Say($"  endpoint: {(peer.Endpoint is null ? "(none)" : peer.Endpoint.ToString())}");
            Terminal.Say($"  allowed ips: {(peer.AllowedIps.Count == 0 ? "(none)" : string.Join(", ", peer.AllowedIps))}");
            Terminal.Say($"  latest handshake: {Ago(peer.LastHandshake)}");
            Terminal.Say($"  transfer: {Size(peer.RxBytes)} received, {Size(peer.TxBytes)} sent");
            Terminal.Say($"  persistent keepalive: {(peer.PersistentKeepalive.IsZero ? "off" : $"every {peer.PersistentKeepalive} seconds")}");
            Terminal.Say($"  advanced security: {(peer.AdvancedSecurity ? "on" : "off")}");
        }
    }

    private static IReadOnlyList<string> Asked(AwgDevices devices, string? name) =>
        name is null ? devices.Names() : [name];

    private static string Private(AwgDevice device, bool secrets) => device.PrivateKey is null
        ? "(none)"
        : secrets ? device.PrivateKey : "(hidden)";

    private static string Junk(AwgObfuscation shape)
    {
        var written = new[] { shape.I1, shape.I2, shape.I3, shape.I4, shape.I5 }
            .Where(item => !string.IsNullOrEmpty(item))
            .ToArray();

        return written.Length == 0 ? "(none)" : string.Join(", ", written);
    }

    private static string Ago(DateTimeOffset? moment)
    {
        if (moment is null)
        {
            return "never";
        }

        var passed = DateTimeOffset.UtcNow - moment.Value;

        return passed < TimeSpan.Zero
            ? moment.Value.ToString("u")
            : $"{(long)passed.TotalSeconds} seconds ago";
    }

    private static string Size(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var step = 0;
        var value = (double)bytes;
        while (value >= 1024 && step < units.Length - 1)
        {
            value /= 1024;
            step++;
        }

        return step == 0 ? $"{bytes} B" : $"{value:0.00} {units[step]}";
    }

    private static int Usage()
    {
        Terminal.Fail("""
            usage:
              device list
              device show [name] [--secrets]
              device set <name> [--key <base64>] [--generate] [--port <number>] [--fwmark <number>]
                          [--shape jc=4,jmin=40,jmax=70,s1=15,h1=1-3,keepalive=25] [--i1 <spec>] [--hpk <base64>]
              device peer <name> <public key> [--allowed <a,b>] [--endpoint <host:port>]
                          [--preshared <base64>] [--keepalive <seconds>] [--replace-ips] [--update-only] [--remove]
            """);

        return 2;
    }
}
