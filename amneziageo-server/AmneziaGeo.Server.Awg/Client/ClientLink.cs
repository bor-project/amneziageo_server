using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.IO.Compression;
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

    private const string Container = "amnezia-awg";

    private static readonly JsonSerializerOptions Plain = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Returns the configuration of a client as an Amnezia vpn:// link.
    /// </summary>
    public static string Link(
        ServerConfig config,
        TunnelClient client,
        ClientTemplate? template = null,
        IReadOnlyList<string>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        var last = new JsonObject
        {
            ["config"] = ClientText.Text(config, client, template, resolver),
            ["hostName"] = config.Host,
            ["port"] = config.ListenPort,
        };
        var document = new JsonObject
        {
            ["containers"] = new JsonArray(new JsonObject
            {
                ["container"] = Container,
                ["awg"] = new JsonObject
                {
                    ["last_config"] = last.ToJsonString(Plain),
                    ["isThirdPartyConfig"] = true,
                    ["port"] = config.ListenPort.ToString(CultureInfo.InvariantCulture),
                    ["transport_proto"] = "udp",
                },
            }),
            ["defaultContainer"] = Container,
            ["description"] = ClientText.Title(config, client),
            ["hostName"] = config.Host,
        };

        return Scheme + Base64Url.EncodeToString(Packed(Encoding.UTF8.GetBytes(document.ToJsonString(Plain))));
    }

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
