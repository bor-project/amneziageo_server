using System.Text.Json;

namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Reads the UDP destination requested by a WebSocket tunnel.
/// </summary>
public static class ProxyWsTarget
{
    private const string Prefix = "authorization.bearer.";

    /// <summary>
    /// Reads the target from the transport token.
    /// </summary>
    public static bool TryRead(string? protocols, out ProxyDestination? destination)
    {
        destination = null;
        if (string.IsNullOrEmpty(protocols) || protocols.Length > 8192)
        {
            return false;
        }

        var tokens = protocols.Split(',').Select(value => value.Trim())
            .Where(value => value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (tokens.Length != 1 || !tokens[0].StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = tokens[0][Prefix.Length..].Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0
            || part.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')))
        {
            return false;
        }

        try
        {
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Duplicate(root)
                || !root.TryGetProperty("r", out var host) || host.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("rp", out var port) || port.ValueKind != JsonValueKind.Number
                || !port.TryGetInt32(out var number) || number is < 1 or > 65535
                || !root.TryGetProperty("p", out var protocol) || protocol.ValueKind != JsonValueKind.Object
                || protocol.EnumerateObject().Count() != 1 || !protocol.TryGetProperty("Udp", out var udp)
                || udp.ValueKind != JsonValueKind.Object || Duplicate(udp)
                || !ProxyTargetRules.TryHost(host.GetString(), out var normalized))
            {
                return false;
            }

            destination = new ProxyDestination(normalized, number);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    private static bool Duplicate(JsonElement element) => element.EnumerateObject()
        .GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1);
}
