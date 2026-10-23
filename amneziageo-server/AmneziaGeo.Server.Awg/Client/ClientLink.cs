using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Writes the vpn:// link the configuration of a client travels in.
/// </summary>
public static class ClientLink
{
    private const string Scheme = "vpn://";

    private const string Obfuscated = "amnezia-awg";

    private const string Bare = "amnezia-wireguard";

    // The settings of the obfuscation, carried beside the file the way AmneziaVPN reads them out of a file of its own.
    private static readonly string[] AwgKeys =
    [
        "Jc", "Jmin", "Jmax", "S1", "S2", "S3", "S4", "H1", "H2", "H3", "H4",
        "I1", "I2", "I3", "I4", "I5", "HeaderProtectionKey", "ContentPaddingAddition",
        "RekeyAfterTime", "RekeyTimeout", "RejectAfterTime", "KeepaliveTimeout",
        "MaxHandshakeAttempts", "RandomTrailers", "DisableCookies",
    ];

    // The settings AmneziaVPN refuses an obfuscated configuration without, with what the protocol takes for a missing one.
    private static readonly (string Key, string Fallback)[] Demanded =
    [
        ("Jc", "0"), ("Jmin", "0"), ("Jmax", "0"), ("S1", "0"), ("S2", "0"),
        ("H1", "1"), ("H2", "2"), ("H3", "3"), ("H4", "4"),
    ];

    private static readonly JsonSerializerOptions Plain = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Returns the configuration of a client as an Amnezia vpn:// link: the file, and beside it the fields AmneziaVPN connects by.
    /// </summary>
    public static string Link(
        ServerConfig config,
        TunnelClient client,
        ClientTemplate? template = null,
        IReadOnlyList<string>? resolver = null,
        string? title = null,
        IReadOnlyList<string>? panel = null,
        DnsFiles? files = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        var text = ClientText.Text(config, client, template, resolver, panel, files);
        var head = ConfigLines.Head(text);
        var peer = Peer(text);
        var obfuscated = AwgKeys.Any(head.ContainsKey);
        var container = obfuscated ? Obfuscated : Bare;

        var last = new JsonObject
        {
            ["config"] = text,
            ["hostName"] = config.Host,
            ["port"] = config.ListenPort,
            ["client_priv_key"] = ConfigLines.Value(head, "PrivateKey"),
            ["client_ip"] = ConfigLines.Value(head, "Address"),
            ["server_pub_key"] = ConfigLines.Value(peer, "PublicKey"),
            ["allowed_ips"] = Listed(ConfigLines.Value(peer, "AllowedIPs")),
        };
        Take(last, "psk_key", ConfigLines.Value(peer, "PresharedKey"));
        Take(last, "mtu", ConfigLines.Value(head, "MTU"));
        Take(last, "persistent_keep_alive", ConfigLines.Value(peer, "PersistentKeepalive"));
        if (obfuscated)
        {
            foreach (var key in AwgKeys)
            {
                Take(last, key, ConfigLines.Value(head, key));
            }

            foreach (var (key, fallback) in Demanded)
            {
                last[key] ??= fallback;
            }
        }

        var document = new JsonObject
        {
            ["containers"] = new JsonArray(new JsonObject
            {
                ["container"] = container,
                [obfuscated ? "awg" : "wireguard"] = new JsonObject
                {
                    ["last_config"] = last.ToJsonString(Plain),
                    ["isThirdPartyConfig"] = true,
                    ["port"] = config.ListenPort.ToString(CultureInfo.InvariantCulture),
                    ["transport_proto"] = "udp",
                },
            }),
            ["defaultContainer"] = container,
            ["description"] = title ?? ClientText.Title(config, client),
            ["hostName"] = config.Host,
        };

        // AmneziaVPN asks the two servers it is named and nothing else, so one server is named twice.
        var servers = ConfigLines.Parts(ConfigLines.Value(head, "DNS")).Where(IsV4).ToList();
        if (servers.Count > 0)
        {
            document["dns1"] = servers[0];
            document["dns2"] = servers.Count > 1 ? servers[1] : servers[0];
        }

        return Scheme + Base64Url.EncodeToString(Packed(Encoding.UTF8.GetBytes(document.ToJsonString(Plain))));
    }

    private static IReadOnlyDictionary<string, string> Peer(string text)
    {
        var blocks = ConfigLines.Blocks(text);

        return blocks.Count > 0 ? blocks[0].Values : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static JsonArray Listed(string ranges)
    {
        var listed = new JsonArray();
        foreach (var range in ConfigLines.Parts(ranges))
        {
            listed.Add(range);
        }

        return listed;
    }

    private static void Take(JsonObject fields, string key, string value)
    {
        if (value.Length > 0)
        {
            fields[key] = value;
        }
    }

    private static bool IsV4(string text) =>
        IPAddress.TryParse(text, out var address) && address.AddressFamily == AddressFamily.InterNetwork;

    private static byte[] Packed(byte[] data)
    {
        var size = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, data.Length);

        using var output = new MemoryStream();
        output.Write(size);
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
