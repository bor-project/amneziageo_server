using System.Globalization;
using System.Text;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// Reads the clients an endpoint already carries.
/// </summary>
public static class ClientImport
{
    /// <summary>
    /// Reads the peers of an interface file into clients of an endpoint.
    /// </summary>
    public static IReadOnlyList<TunnelClient> Peers(string? text, long configId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var clients = new List<TunnelClient>();
        var blocks = ConfigLines.Blocks(text);
        for (var index = 0; index < blocks.Count; index++)
        {
            var values = blocks[index].Values;
            var key = ConfigLines.Value(values, "PublicKey");
            if (!Curve25519.IsKey(key))
            {
                continue;
            }

            clients.Add(new TunnelClient
            {
                ConfigId = configId,
                Name = Name(blocks[index].Title, index + 1),
                PublicKey = key,
                PresharedKey = ConfigLines.Value(values, "PresharedKey"),
                Address = ConfigLines.Parts(ConfigLines.Value(values, "AllowedIPs")),
                IsEnabled = true,
            });
        }

        return clients;
    }

    /// <summary>
    /// Returns a name that holds, out of the one a client was listed under.
    /// </summary>
    public static string Name(string? title, int number)
    {
        var body = new StringBuilder();
        foreach (var letter in (title ?? string.Empty).Trim())
        {
            body.Append(char.IsAsciiLetterOrDigit(letter) || letter is '.' or '-' or '_' ? letter : '-');
        }

        var name = body.ToString().Trim('-');

        return name.Length > 0 && char.IsAsciiLetterOrDigit(name[0])
            ? name[..Math.Min(name.Length, ClientRules.MaxNameLength)]
            : "peer-" + number.ToString(CultureInfo.InvariantCulture);
    }
}
