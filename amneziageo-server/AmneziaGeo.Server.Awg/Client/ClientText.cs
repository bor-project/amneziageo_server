using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Awg.Config;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Writes the configuration a client connects with.
/// </summary>
public static class ClientText
{
    /// <summary>
    /// Returns the configuration of a client as it is handed out.
    /// </summary>
    public static string Text(
        ServerConfig config,
        TunnelClient client,
        ClientTemplate? template = null,
        int helloPort = 0,
        IReadOnlyList<string>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        var dns = Names(config, template, resolver);
        var ranges = template is null
            ? config.AllowedIps
            : Either(template.AllowedIps, TemplateDefaults.AllowedIps(client.Address));
        var mtu = template is null ? config.Mtu : template.Mtu ?? TemplateDefaults.Mtu;
        var keepalive = template is null ? config.Keepalive : template.Keepalive ?? TemplateDefaults.Keepalive;

        var text = new StringBuilder();
        text.Append("[Interface]\n");
        Line(text, "PrivateKey", client.PrivateKey);
        Line(text, "Address", string.Join(", ", client.Address));
        Line(text, "DNS", string.Join(", ", dns));
        if (mtu > 0)
        {
            Line(text, "MTU", Number(mtu));
        }

        Obfuscation(text, config.Obfuscation);
        Line(text, "# AmneziaGeo Api", string.Join(", ", ApiPoints(config, helloPort)));
        Reverse(text, config, client);

        text.Append("\n[Peer]\n");
        Line(text, "PublicKey", config.PublicKey);
        Line(text, "PresharedKey", Preshared(config, client));
        Line(text, "AllowedIPs", string.Join(", ", ranges));
        Line(text, "Endpoint", Endpoint(config));
        if (keepalive > 0)
        {
            Line(text, "PersistentKeepalive", Number(keepalive));
        }

        return text.ToString();
    }

    /// <summary>
    /// Returns the addresses inside the tunnel the point of the server answers a client at.
    /// </summary>
    public static IReadOnlyList<string> ApiPoints(ServerConfig config, int helloPort = 0)
    {
        ArgumentNullException.ThrowIfNull(config);

        var port = Number(config.HelloPort(helloPort));
        var points = new List<string>();
        foreach (var range in config.Address)
        {
            if (!IPAddress.TryParse(range.Split('/')[0].Trim(), out var address))
            {
                continue;
            }

            var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
            var point = host + ":" + port;
            if (!points.Contains(point, StringComparer.Ordinal))
            {
                points.Add(point);
            }
        }

        return points;
    }

    /// <summary>
    /// Returns the name the configuration of a client is saved under.
    /// </summary>
    public static string FileName(ServerConfig config, TunnelClient client)
    {
        return $"{Title(config, client)}.conf";
    }

    /// <summary>
    /// Returns the name the configuration of a client goes by.
    /// </summary>
    public static string Title(ServerConfig config, TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        return $"{config.Name}-{client.Name}";
    }

    /// <summary>
    /// Returns the key a client adds to the handshake.
    /// </summary>
    public static string Preshared(ServerConfig config, TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        return client.PresharedKey.Length > 0 ? client.PresharedKey : config.PresharedKey;
    }

    /// <summary>
    /// Returns the address and port a client sends packets to.
    /// </summary>
    public static string Endpoint(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Host.Length == 0)
        {
            return string.Empty;
        }

        var host = IPAddress.TryParse(config.Host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{config.Host}]"
            : config.Host;

        return $"{host}:{Number(config.ListenPort)}";
    }

    /// <summary>
    /// Writes what the application of the client turns on for the traffic that comes from the tunnel.
    /// </summary>
    private static void Reverse(StringBuilder text, ServerConfig config, TunnelClient client)
    {
        var inbound = InboundName.Taken(client.Inbound, config.Inbound);
        if (inbound != ClientInbound.Off)
        {
            Line(text, "# AmneziaGeo Inbound", InboundName.Of(inbound));
        }

        if (client.Routes.Count > 0)
        {
            Line(text, "# AmneziaGeo Routes", string.Join(", ", client.Routes));
        }
    }

    private static IReadOnlyList<string> Either(IReadOnlyList<string> own, IReadOnlyList<string> otherwise) =>
        own.Count > 0 ? own : otherwise;

    private static IReadOnlyList<string> Names(ServerConfig config, ClientTemplate? template, IReadOnlyList<string>? resolver)
    {
        var own = resolver is { Count: > 0 } ? resolver : null;

        return template is null
            ? own ?? config.Dns
            : Either(template.Dns, own ?? TemplateDefaults.Dns);
    }

    private static void Obfuscation(StringBuilder text, ObfuscationSettings obfuscation)
    {
        Size(text, "Jc", obfuscation.Jc);
        Size(text, "Jmin", obfuscation.Jmin);
        Size(text, "Jmax", obfuscation.Jmax);
        Size(text, "S1", obfuscation.S1);
        Size(text, "S2", obfuscation.S2);
        Size(text, "S3", obfuscation.S3);
        Size(text, "S4", obfuscation.S4);
        Line(text, "H1", obfuscation.H1);
        Line(text, "H2", obfuscation.H2);
        Line(text, "H3", obfuscation.H3);
        Line(text, "H4", obfuscation.H4);
        Line(text, "I1", obfuscation.I1);
        Line(text, "I2", obfuscation.I2);
        Line(text, "I3", obfuscation.I3);
        Line(text, "I4", obfuscation.I4);
        Line(text, "I5", obfuscation.I5);
        Line(text, "HeaderProtectionKey", obfuscation.HeaderProtectionKey);
        Line(text, "ContentPaddingAddition", obfuscation.ContentPaddingAddition);
        Line(text, "RekeyAfterTime", obfuscation.RekeyAfterTime);
        Line(text, "RekeyTimeout", obfuscation.RekeyTimeout);
        Line(text, "RejectAfterTime", obfuscation.RejectAfterTime);
        Line(text, "KeepaliveTimeout", obfuscation.KeepaliveTimeout);
        Line(text, "MaxHandshakeAttempts", obfuscation.MaxHandshakeAttempts);
        if (obfuscation.RandomTrailers)
        {
            Line(text, "RandomTrailers", "on");
        }

        if (obfuscation.DisableCookies)
        {
            Line(text, "DisableCookies", "on");
        }
    }

    private static void Size(StringBuilder text, string key, int value)
    {
        if (value > 0)
        {
            Line(text, key, Number(value));
        }
    }

    private static void Line(StringBuilder text, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            text.Append(key).Append(" = ").Append(value).Append('\n');
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
