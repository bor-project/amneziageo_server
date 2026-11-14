using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmneziaGeo.Server.Core.Proxy;

/// <summary>
/// Describes a UDP destination.
/// </summary>
public sealed record ProxyDestination(string Host, int Port);

/// <summary>
/// Describes the destinations a proxy accepts.
/// </summary>
public sealed record ProxyTargets(bool Restricted, IReadOnlyList<ProxyDestination> Destinations, bool Local = false);

/// <summary>
/// Validates and compares proxy destinations.
/// </summary>
public static class ProxyTargetRules
{
    public const int Maximum = 64;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ProxyTargets Denied { get; } = new(true, []);

    public static ProxyTargets LocalOnly { get; } = new(true, [], true);

    /// <summary>
    /// Returns the destination mode.
    /// </summary>
    public static string Mode(ProxyTargets? targets) => targets is { Local: true } ? "local"
        : targets is { Restricted: true } ? "selected" : "any";

    /// <summary>
    /// Expands the local interface destinations.
    /// </summary>
    public static ProxyTargets? Expand(ProxyTargets? targets, IReadOnlyList<ProxyDestination> local)
    {
        if (!TryNormalize(targets, out var policy))
        {
            return Denied;
        }

        if (policy is not { Local: true })
        {
            return policy;
        }

        return TryNormalize(new ProxyTargets(true, local), out var expanded) ? expanded : Denied;
    }

    /// <summary>
    /// Normalizes a destination host.
    /// </summary>
    public static bool TryHost(string? value, out string host)
    {
        host = string.Empty;
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Any(char.IsControl) || text.IndexOfAny(['/', '\\', '%', '*', '@', '?', '#']) >= 0)
        {
            return false;
        }

        var bracketed = text.StartsWith('[') && text.EndsWith(']');
        if (bracketed)
        {
            text = text[1..^1];
        }

        if (IPAddress.TryParse(text, out var address))
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                return false;
            }

            host = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString().ToLowerInvariant();
            return true;
        }

        if (bracketed || text.Contains(':', StringComparison.Ordinal) || text.Contains('[', StringComparison.Ordinal)
            || text.Contains(']', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var domain = text.EndsWith('.') ? text[..^1] : text;
            var ascii = new IdnMapping().GetAscii(domain.ToLowerInvariant()).ToLowerInvariant();
            var labels = ascii.Split('.');
            if (ascii.Length is 0 or > 253 || labels.Any(label => label.Length is 0 or > 63
                || label[0] == '-' || label[^1] == '-'
                || label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
            {
                return false;
            }

            host = ascii;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes a target policy.
    /// </summary>
    public static bool TryNormalize(ProxyTargets? targets, out ProxyTargets? normalized)
    {
        normalized = null;
        if (targets is null)
        {
            return true;
        }

        if (targets.Destinations is null || targets.Destinations.Count > 1024)
        {
            return false;
        }

        var unique = new Dictionary<(string Host, int Port), ProxyDestination>();
        foreach (var destination in targets.Destinations ?? [])
        {
            if (destination is null || destination.Port is < 1 or > 65535
                || !TryHost(destination.Host, out var host))
            {
                return false;
            }

            unique[(host, destination.Port)] = new ProxyDestination(host, destination.Port);
            if (unique.Count > Maximum)
            {
                return false;
            }
        }

        if (targets.Local && (!targets.Restricted || unique.Count != 0)
            || targets.Restricted && unique.Count == 0 && !targets.Local)
        {
            return false;
        }

        normalized = new ProxyTargets(targets.Restricted, [.. unique.Values
            .OrderBy(destination => destination.Host, StringComparer.Ordinal).ThenBy(destination => destination.Port)], targets.Local);
        return true;
    }

    /// <summary>
    /// Reads a stored target policy.
    /// </summary>
    public static ProxyTargets? Read(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("restricted", out var restricted) || restricted.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("destinations", out var destinations) || destinations.ValueKind != JsonValueKind.Array
                || root.TryGetProperty("local", out var local) && local.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return Denied;
            }

            var policy = JsonSerializer.Deserialize<ProxyTargets>(text, Json);
            return policy is not null && TryNormalize(policy, out var normalized) ? normalized : Denied;
        }
        catch (JsonException)
        {
            return Denied;
        }
    }

    /// <summary>
    /// Writes a target policy.
    /// </summary>
    public static string Write(ProxyTargets? targets) => targets is null ? string.Empty : JsonSerializer.Serialize(targets, Json);

    /// <summary>
    /// Matches the equivalent forms of a destination host.
    /// </summary>
    public static string HostPattern(string host)
    {
        var unicode = new IdnMapping().GetUnicode(host);
        var choices = new[] { host, unicode }.Distinct(StringComparer.OrdinalIgnoreCase).Select(Regex.Escape);
        return "^(?i:(?:" + string.Join("|", choices) + "))\\.?$";
    }

    /// <summary>
    /// Checks a UDP destination.
    /// </summary>
    public static bool Allows(ProxyTargets? targets, string? host, int port)
    {
        if (port is < 1 or > 65535 || !TryHost(host, out var normalized))
        {
            return false;
        }

        if (!TryNormalize(targets, out var policy) || policy is { Local: true })
        {
            return false;
        }

        if (policy is null || !policy.Restricted)
        {
            return true;
        }

        return policy.Destinations.Any(destination =>
            destination.Port == port && string.Equals(destination.Host, normalized, StringComparison.Ordinal));
    }
}
