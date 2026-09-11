using System.Globalization;
using System.Text;
using System.Text.Json;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Awg.Client;

/// <summary>
/// The clients a text was read into, and why it was refused.
/// </summary>
public sealed record ClientImportRead(IReadOnlyList<TunnelClient> Clients, ConfigFault? Fault);

/// <summary>
/// A client an import took under another name.
/// </summary>
public sealed record ClientRename(string From, string To);

/// <summary>
/// A client an import turned away, with the reason.
/// </summary>
public sealed record ClientRefusal(string Name, string Error, string Message);

/// <summary>
/// What an import of many clients produced.
/// </summary>
public sealed record ClientImportReport(
    int Taken,
    int Held,
    IReadOnlyList<ClientRename> Renamed,
    IReadOnlyList<ClientRefusal> Refused);

/// <summary>
/// Reads the clients an endpoint already carries.
/// </summary>
public static class ClientImport
{
    /// <summary>
    /// Reads the peers of an interface file or the file a host keeps its clients in into clients of an endpoint.
    /// </summary>
    public static ClientImportRead Read(string? text, string endpoint, long configId, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Refuse("the text is empty");
        }

        var clients = text.TrimStart().StartsWith('{') ? Kept(text, endpoint, configId, prefix) : Peers(text, configId);

        return clients.Count > 0
            ? new ClientImportRead(clients, null)
            : Refuse($"the text carries no client of '{endpoint}'");
    }

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
    /// Reads the file a host keeps its clients in, taking the part named after the endpoint or its only part.
    /// </summary>
    public static IReadOnlyList<TunnelClient> Kept(string text, string endpoint, long configId, string? prefix)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return Part(document.RootElement, endpoint) is { } part ? Clients(part, configId, prefix) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads one part of the file a host keeps its clients in into clients of an endpoint.
    /// </summary>
    public static IReadOnlyList<TunnelClient> Clients(JsonElement part, long configId, string? prefix)
    {
        var clients = new List<TunnelClient>();
        if (part.ValueKind != JsonValueKind.Object)
        {
            return clients;
        }

        var number = 0;
        foreach (var record in part.EnumerateObject())
        {
            number++;
            var body = record.Value;
            if (body.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = Text(body, "publicKey");
            if (key.Length == 0)
            {
                continue;
            }

            var addresses = new List<string>();
            if (body.TryGetProperty("allowedIPs", out var ranges) && ranges.ValueKind == JsonValueKind.Array)
            {
                addresses.AddRange(ranges.EnumerateArray().Select(one => one.ValueKind == JsonValueKind.String ? one.GetString() ?? string.Empty : string.Empty));
            }

            if (prefix is { Length: > 0 })
            {
                addresses.AddRange(addresses.ToArray().Select(one => Sixth(prefix, one)).Where(one => one.Length > 0));
            }

            clients.Add(new TunnelClient
            {
                ConfigId = configId,
                Name = Name(Text(body, "email") is { Length: > 0 } email ? email : record.Name, number),
                PrivateKey = Text(body, "privateKey"),
                PublicKey = key,
                PresharedKey = Text(body, "preSharedKey"),
                Address = [.. addresses.Where(one => one.Length > 0)],
                IsEnabled = !body.TryGetProperty("enable", out var enabled) || enabled.ValueKind != JsonValueKind.False,
            });
        }

        return clients;
    }

    /// <summary>
    /// Returns the address a client carries in a range of the second family.
    /// </summary>
    public static string Sixth(string prefix, string address)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(address);

        var bare = address.Split('/')[0];
        var mark = bare.LastIndexOf('.');
        if (mark < 0 || !int.TryParse(bare[(mark + 1)..], out var last))
        {
            return string.Empty;
        }

        return $"{prefix}:{last.ToString("x", CultureInfo.InvariantCulture)}/128";
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

    private static JsonElement? Part(JsonElement root, string endpoint)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var parts = root.EnumerateObject().ToList();
        var named = parts
            .Where(one => string.Equals(one.Name, endpoint, StringComparison.Ordinal))
            .Select(one => (JsonElement?)one.Value)
            .FirstOrDefault();

        return named ?? (parts.Count == 1 ? parts[0].Value : default(JsonElement?));
    }

    private static string Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString() ?? string.Empty
            : string.Empty;

    private static ClientImportRead Refuse(string message) => new([], new ConfigFault("bad-client-import", message));
}
