using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;

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
        IReadOnlyList<string>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);

        // A field the template leaves empty takes what the endpoint holds, and only then the built in value.
        var dns = Names(config, template, resolver);
        var ranges = template is null
            ? config.AllowedIps
            : Either(template.AllowedIps, Either(config.AllowedIps, TemplateDefaults.AllowedIps(client.Address)));
        var mtu = template?.Mtu ?? config.Mtu;
        var keepalive = template?.Keepalive ?? config.Keepalive;

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
        if (ConfigServices.Moved(config))
        {
            Line(text, "# AmneziaGeo Services", Number(config.ServicesPort));
        }

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
    /// Returns the name the configuration of a client is saved under.
    /// </summary>
    public static string FileName(ServerConfig config, TunnelClient client, string template = ConfigName.Default, string host = "")
    {
        return $"{ConfigName.File(Title(config, client, template, host))}.conf";
    }

    /// <summary>
    /// Returns the name the configuration of a client goes by.
    /// </summary>
    public static string Title(ServerConfig config, TunnelClient client, string template = ConfigName.Default, string host = "")
    {
        ArgumentNullException.ThrowIfNull(template);

        var name = ConfigName.Fill(template, Values(config, client, host));

        return name.Length > 0 ? name : Stamp(client);
    }

    /// <summary>
    /// Returns what the substitutions of the name template stand for with a client.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Values(ServerConfig config, TunnelClient client, string host = "")
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(host);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOST"] = config.Host.Length > 0 ? config.Host : host,
            ["INTERFACE"] = config.Name,
            ["CLIENT"] = client.Name,
            ["ID"] = client.Id.ToString(CultureInfo.InvariantCulture),
            ["PORT"] = Number(config.ListenPort),
            ["NOTE"] = client.Note,
            ["DATE"] = client.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Returns when a client was added, down to the millisecond, as the name of a configuration nothing else names.
    /// </summary>
    public static string Stamp(TunnelClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return client.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd-HH-mm-ss-fff", CultureInfo.InvariantCulture);
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

    private static IReadOnlyList<string> Either(IReadOnlyList<string> own, IReadOnlyList<string> otherwise) =>
        own.Count > 0 ? own : otherwise;

    private static IReadOnlyList<string> Names(ServerConfig config, ClientTemplate? template, IReadOnlyList<string>? resolver)
    {
        var own = resolver is { Count: > 0 } ? resolver : null;

        return template is null
            ? own ?? config.Dns
            : Either(template.Dns, own ?? Either(config.Dns, TemplateDefaults.Dns));
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
